using ADALotto.Models;
using ADALottoModels;
using ADALottoModels.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ADALotto.ClientLib
{
    public class ALGame
    {
        #region Properties
        private ADALottoClient ADALottoClient { get; set; }
        private const long HARD_CHECKPOINT = 4934993;
        private const long BLOCK_CRAWL_COUNT = 70;
        public static string Version => "0.1.7-alpha";
        private bool IsSyncing { get; set; } = false;
        public bool IsInitialSyncFinished { get; set; } = false;
        public bool IsGameRunning => GameState?.GameGenesisTx != null;
        private Block LatestNetworkBlock { get; set; } = new Block();
        private Block PreviousNetworkBlock { get; set; } = new Block();
        public ALGameState GameState { get; set; } = new ALGameState();
        public IEnumerable<ALWinningBlock> Combination { get; private set; } = new List<ALWinningBlock>();
        public TimeSpan RemainingRoundTime => GameState.GameGenesisTx != null 
            ? CalculateDrawTime(GameState.StartBlock.BlockNo, GameState.NextDrawBlock.BlockNo) : TimeSpan.FromSeconds(0);

        public double RoundProgress
        {
            get
            {
                var result = 0d;
                if(GameState.GameGenesisTxMeta != null)
                {
                    var totalRoundTime = CalculateDrawTime(GameState.NextDrawBlock.BlockNo - GameState.GameGenesisTxMeta.BlockInterval, GameState.NextDrawBlock.BlockNo);
                    var remainingTime = RemainingRoundTime;
                    result = 100 * (totalRoundTime.TotalSeconds - remainingTime.TotalSeconds) / totalRoundTime.TotalSeconds;
                }
                return result;
            }
        }
        #endregion

        #region Events
        public event EventHandler<EventArgs>? InitialSyncComplete;
        public event EventHandler<EventArgs>? DrawStart;
        public event EventHandler<EventArgs>? DrawEnd;
        public event EventHandler<EventArgs>? Fetch;
        #endregion

        public ALGame(string serverUrl, string hostAddress)
        {
            ADALottoClient = new ADALottoClient(serverUrl, hostAddress);
        }

        public void Start(ALGameState startInfo)
        {
            GameState = startInfo;
            GameState.Version = Version;
            _ = ProcessSyncAsync();
        }

        private async Task ProcessSyncAsync()
        {
            await GetStartBlock();
            GameState.IsRunning = true;
            while (GameState.IsRunning)
            {
                try
                {
                    await FetchAsync();
                }
                catch(Exception ex)
                {
                    IsSyncing = false;
                    IsInitialSyncFinished = false;
                    Console.WriteLine($"Error occured: {ex}");
                }
                await Task.Delay(10000);
            }
        }

        public void Stop()
        {
            GameState.IsRunning = false;
        }

        public async Task<Block> GetLatestNetworkBlockAsync()
        {
            var lnb = await ADALottoClient.GetLatestBlockAsync();
            if (lnb != null)
            {
                LatestNetworkBlock = lnb;
            }
            return LatestNetworkBlock;
        }


        private async Task FetchAsync()
        {
            if (!IsSyncing)
            {
                IsSyncing = true;
                await GetLatestNetworkBlockAsync();
                if (LatestNetworkBlock.BlockNo != PreviousNetworkBlock.BlockNo)
                {
                    while (GameState.StartBlock.BlockNo < LatestNetworkBlock.BlockNo)
                    {
                        if (GameState.GameGenesisTx != null && GameState.GameGenesisTxMeta != null)
                        {
                            var ticketCount = 0;
                            if (GameState.StartBlock.BlockNo < GameState.NextDrawBlock.BlockNo && GameState.NextDrawBlock.BlockNo <= Math.Min(GameState.StartBlock.BlockNo + BLOCK_CRAWL_COUNT, LatestNetworkBlock.BlockNo))
                            {
                                GameState.IsDrawing = true;
                                DrawStart?.Invoke(this, new EventArgs());
                                var endBlock = await ADALottoClient.GetBlockInfo(Math.Min(GameState.NextDrawBlock.BlockNo, LatestNetworkBlock.BlockNo) - 1);
                                ticketCount = await ADALottoClient.GetTPTxCountAsync(
                                    GameState.StartBlock,
                                    endBlock,
                                    GameState.GameGenesisTxMeta.TicketPrice);
                            }
                            else if (!GameState.IsDrawing)
                            {
                                var endBlock = await ADALottoClient.GetBlockInfo(Math.Min(GameState.StartBlock.BlockNo + BLOCK_CRAWL_COUNT, LatestNetworkBlock.BlockNo) - 1);
                                ticketCount = await ADALottoClient.GetTPTxCountAsync(
                                    GameState.StartBlock,
                                    endBlock,
                                    GameState.GameGenesisTxMeta.TicketPrice);
                            }
                            GameState.CurrentPot += (long)(ticketCount * GameState.GameGenesisTxMeta.TicketPrice * GameState.GameGenesisTxMeta.WinnerPrizeRatio / 100);

                            if (GameState.IsDrawing)
                            {
                                var startBlock = await ADALottoClient.GetBlockInfo(Math.Max(GameState.StartBlock.BlockNo, GameState.NextDrawBlock.BlockNo));
                                var endBlock = await ADALottoClient.GetBlockInfo(Math.Min(GameState.StartBlock.BlockNo + BLOCK_CRAWL_COUNT, LatestNetworkBlock.BlockNo) - 1);
                                GameState.NextRoundTicketCount += await ADALottoClient.GetTPTxCountAsync(
                                    startBlock,
                                    endBlock,
                                    GameState.GameGenesisTxMeta.TicketPrice);

                                Combination = await GetWinningBlocksAsync(GameState.NextDrawBlock.BlockNo, GameState.GameGenesisTxMeta.Digits);

                                if (Combination.Count() == GameState.GameGenesisTxMeta.Digits)
                                {
                                    var drawBlockInfo = await ADALottoClient.GetBlockInfo(GameState.NextDrawBlock.BlockNo);
                                    if (drawBlockInfo != null)
                                    {
                                        endBlock = await ADALottoClient.GetBlockInfo(GameState.NextDrawBlock.BlockNo - 1);
                                        var winningTPtxes = await ADALottoClient.GetWinningTPTxesAsync(
                                            GameState.PrevDrawBlock,
                                            endBlock,
                                            GameState.GameGenesisTxMeta.TicketPrice,
                                            Combination.Select(wb => int.Parse(wb.Number)));
                                        UpdatePreviousResults(Combination.ToList(), drawBlockInfo, winningTPtxes.Count());

                                        if (winningTPtxes.Count() > 0)
                                        {
                                            await UpdatePreviousWinnersAsync(winningTPtxes, drawBlockInfo);
                                            GameState.CurrentPot = 0;
                                            GameState.GameGenesisTx = null;
                                            GameState.GameGenesisTxMeta = null;
                                            GameState.NextDrawBlock = new Block();
                                            GameState.PrevDrawBlock = new Block();
                                        }
                                        else
                                        {
                                            GameState.PrevDrawBlock = GameState.NextDrawBlock;
                                            GameState.NextDrawBlock = new Block { BlockNo = GameState.NextDrawBlock.BlockNo + GameState.GameGenesisTxMeta.BlockInterval };
                                            GameState.CurrentPot += (long)(GameState.NextRoundTicketCount * GameState.GameGenesisTxMeta.TicketPrice * GameState.GameGenesisTxMeta.WinnerPrizeRatio / 100);
                                            GameState.NextRoundTicketCount = 0;
                                        }
                                    }
                                    GameState.IsDrawing = false;
                                    DrawEnd?.Invoke(this, new EventArgs());
                                }
                            }
                        }
                        
                        
                        var searchEndBlock = await ADALottoClient.GetBlockInfo(Math.Min(GameState.StartBlock.BlockNo + BLOCK_CRAWL_COUNT, LatestNetworkBlock.BlockNo) - 1);
                        var ggTx = await ADALottoClient.GetGameGenesisTxAsync(GameState.StartBlock, searchEndBlock);
                        if (ggTx != null && ggTx?.Block1 != null)
                        {
                            if (GameState.GameGenesisTx?.Block1 != null)
                            {
                                GameState.PrevDrawBlock = GameState.GameGenesisTx.Block1;
                                GameState.IsDrawing = false;
                                DrawEnd?.Invoke(this, new EventArgs());
                            }
                            else
                            {
                                GameState.PrevDrawBlock = ggTx.Block1;
                            }

                            var ggTxMeta = GetMetaFromGGTx(ggTx);
                            if (ggTxMeta != null)
                            {
                                GameState.GameGenesisTx = ggTx;
                                GameState.GameGenesisTxMeta = ggTxMeta;
                                GameState.CurrentPot = ggTxMeta.BasePrize;
                                GameState.NextDrawBlock = new Block { BlockNo = ggTx.Block1.BlockNo + ggTxMeta.BlockInterval };

                                var ticketCount = await ADALottoClient.GetTPTxCountAsync(
                                    GameState.GameGenesisTx.Block1,
                                    searchEndBlock,
                                    GameState.GameGenesisTxMeta.TicketPrice);
                                GameState.CurrentPot += (long)(ticketCount * GameState.GameGenesisTxMeta.TicketPrice * GameState.GameGenesisTxMeta.WinnerPrizeRatio / 100);
                            }
                        }

                        GameState.StartBlock = LatestNetworkBlock.BlockNo <= GameState.StartBlock.BlockNo + BLOCK_CRAWL_COUNT
                            ? LatestNetworkBlock : await ADALottoClient.GetBlockInfo(GameState.StartBlock.BlockNo + BLOCK_CRAWL_COUNT);

                        
                        await ProcessRewardStatusAsync();
                        Console.WriteLine(GameState.StartBlock.BlockNo);
                        Fetch?.Invoke(this, new EventArgs());
                    }

                }
                IsSyncing = false;
                PreviousNetworkBlock = LatestNetworkBlock;
                if (!IsInitialSyncFinished)
                {
                    IsInitialSyncFinished = true;
                    InitialSyncComplete?.Invoke(this, new EventArgs());
                }
            }
        }

        public async Task ProcessRewardStatusAsync()
        {
            if(GameState.PreviousWinners != null)
            {
                foreach(var winner in GameState.PreviousWinners)
                {
                    if(winner.RewardTx == null)
                    {
                        winner.RewardTx = await ADALottoClient.GetRewardTxAsync(winner.DrawBlock, LatestNetworkBlock, winner.Prize, winner.Address);
                    }
                }
            }
        }

        public void ClearCombination()
        {
            Combination = Enumerable.Empty<ALWinningBlock>();
        }

        private void UpdatePreviousResults(IEnumerable<ALWinningBlock> winningBlocks, Block blockInfo, int winnerCount)
        {
            var result = new ALResult
            {
                DrawDate = blockInfo.Time,
                Numbers = winningBlocks.ToList(),
                Prize = GameState.CurrentPot,
                WinnerCount = winnerCount
            };

            var resultsList = GameState.PreviousResults?.ToList() ?? new List<ALResult>();
            resultsList.Insert(0, result);
            if(resultsList.Count > 10) resultsList.Remove(resultsList.Last());
            GameState.PreviousResults = resultsList;
        }

        private async Task UpdatePreviousWinnersAsync(IEnumerable<Transaction> tpTxes, Block blockInfo)
        {
            foreach (var tpTx in tpTxes)
            {
                if (tpTx.Id != null)
                {
                    var address = await ADALottoClient.GetTxSenderAddressAsync((long)tpTx.Id);
                    var winner = GameState.PreviousWinners?.Where(w => w.Address == address).FirstOrDefault();
                    var winnerList = GameState.PreviousWinners?.ToList() ?? new List<ALWinner>();
                    if(winner == null)
                    {
                        var newWinner = new ALWinner
                        {
                            Address = address,
                            Prize = GameState.CurrentPot / tpTxes.Count(),
                            DrawBlock = blockInfo
                        };
                        winnerList.Insert(0, newWinner);
                    }
                    else
                    {
                        winner.Prize += GameState.CurrentPot / tpTxes.Count();
                    }

                    if(winnerList.Count > 10) winnerList.Remove(winnerList.Last());
                    GameState.PreviousWinners = winnerList;
                }
            }
        }

        public async Task<List<ALWinningBlock>> GetWinningBlocksAsync(long startBlock, int digits)
        {
            var winningBlocks = await ADALottoClient.GetWinningBlocksAsync(startBlock);
            return winningBlocks
            .Select(block =>
            {
                var sizeString = block.Size.ToString().PadLeft(2, '0');
                return new ALWinningBlock
                {
                    Hash = String.Concat(block.Hash.Select(b => b.ToString("x2"))),
                    Number = sizeString[^2..]
                };
            })
            .Where(num => num.Number != "00")
            .GroupBy(x => x.Number)
            .Select(x => x.FirstOrDefault())
            .Take(digits)
            .ToList();
        }


        public async Task<Block> GetStartBlock()
        {
            GameState.StartBlock = await ADALottoClient.GetBlockInfo(Math.Max(HARD_CHECKPOINT, GameState.StartBlock.BlockNo));
            await GetLatestNetworkBlockAsync();
            var refBlock = LatestNetworkBlock;
            var currentBlock = refBlock;
            var roundCount = 0;

            while (GameState.StartBlock.BlockNo < currentBlock.BlockNo)
            {
                var startBlock = await ADALottoClient.GetBlockInfo(currentBlock.BlockNo - BLOCK_CRAWL_COUNT - 1);
                var ggTx = await ADALottoClient.GetGameGenesisTxAsync(startBlock, currentBlock);
                if (ggTx != null && ggTx.Block1 != null)
                {
                    var ggTxMeta = GetMetaFromGGTx(ggTx);
                    if (ggTxMeta != null)
                    {
                        roundCount += (int)(refBlock.BlockNo - ggTx.Block1.BlockNo) / ggTxMeta.BlockInterval;
                        refBlock = ggTx.Block1;
                    }

                    if (roundCount >= 10)
                    {
                        GameState.StartBlock = ggTx.Block1;
                        return GameState.StartBlock;
                    }
                }
                currentBlock = await ADALottoClient.GetBlockInfo(currentBlock.BlockNo - BLOCK_CRAWL_COUNT);
                Console.WriteLine(currentBlock.BlockNo);
            }
            return await ADALottoClient.GetBlockInfo(HARD_CHECKPOINT);
        }

        public ALGameGenesisTxMeta? GetMetaFromGGTx(Transaction ggTx)
        {
            if (ggTx?.TxMetadata?.FirstOrDefault()?.Json != null)
                return JsonSerializer.Deserialize<ALGameGenesisTxMeta>(ggTx.TxMetadata.FirstOrDefault().Json);
            else
                return null;
        }

        public async Task<Dictionary<string, string>> GetTicketsByAddressAsync(string senderAddress, int limit = 10)
        {
            var result = new Dictionary<string, string>();
            if(GameState.GameGenesisTx != null && GameState.GameGenesisTxMeta != null)
            {
                var endBlock = LatestNetworkBlock.BlockNo < GameState.NextDrawBlock.BlockNo ? LatestNetworkBlock : await ADALottoClient.GetBlockInfo(GameState.NextDrawBlock.BlockNo);
                var tpTxes = await ADALottoClient.GetTicketPurchaseTxAsync(senderAddress, GameState.PrevDrawBlock, endBlock, GameState.GameGenesisTxMeta.TicketPrice, limit);
                if(tpTxes != null)
                {
                    foreach(var tx in tpTxes)
                    {
                        var tpTxMetaString = tx?.TxMetadata?.FirstOrDefault()?.Json;
                        if(tx != null && tpTxMetaString != null)
                        {
                            var tpTxMeta = JsonSerializer.Deserialize<ALGameTicketTxMeta>(tpTxMetaString);
                            if(tpTxMeta?.Combination != null)
                            {
                                result.Add(String.Concat(tx.Hash.Select(b => b.ToString("x2"))), String.Join("-",tpTxMeta.Combination.ToString()));
                            }
                        }
                    }
                }
            }
            return result;
        }

        public static TimeSpan CalculateDrawTime(long startBlockNo, long endBlockNo)
        {
            var timeDiff = (endBlockNo - startBlockNo) * 20;
            return TimeSpan.FromSeconds(timeDiff);
        }
    }
}
