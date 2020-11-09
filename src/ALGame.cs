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
        private const long HARD_CHECKPOINT = 4949715;
        private const long BLOCK_CRAWL_COUNT = 15;
        private bool IsSyncing { get; set; } = false;
        private bool IsInitialSyncFinished { get; set; } = false;
        private Block LatestNetworkBlock { get; set; } = new Block();
        public ALGameState GameState { get; set; } = new ALGameState();
        public IEnumerable<ALWinningBlock> Combination { get; set; } = new List<ALWinningBlock>();
        public TimeSpan TotalRoundTime => CalculateDrawTime(GameState.StartBlock, GameState.NextDrawBlock);
        #endregion

        #region Events
        public event EventHandler<EventArgs>? InitialSyncComplete;
        public event EventHandler<EventArgs>? DrawStart;
        public event EventHandler<EventArgs>? DrawEnd;
        public event EventHandler<EventArgs>? OnFetch;
        #endregion

        public ALGame(string serverUrl, string hostAddress)
        {
            ADALottoClient = new ADALottoClient(serverUrl, hostAddress);
        }

        public async Task StartAsync(ALGameState startInfo)
        {
            GameState = startInfo;
            await GetStartBlock();
            GameState.IsRunning = true;
            while (GameState.IsRunning)
            {
                _ = FetchAsync();
                await Task.Delay(20000);
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
                var nextRoundTicketCount = 0;
                while (GameState.StartBlock < LatestNetworkBlock.Id)
                {
                    if (GameState.GameGenesisTx != null && GameState.GameGenesisTxMeta != null)
                    {
                        var ticketCount = 0;
                        if (GameState.StartBlock < GameState.NextDrawBlock && GameState.NextDrawBlock <= GameState.StartBlock + BLOCK_CRAWL_COUNT)
                        {
                            GameState.IsDrawing = true;
                            DrawStart?.Invoke(this, new EventArgs());
                            ticketCount = await ADALottoClient.GetTPTxCountAsync(
                                GameState.StartBlock,
                                GameState.NextDrawBlock - 1,
                                GameState.GameGenesisTxMeta.TicketPrice);
                        }
                        else if (!GameState.IsDrawing)
                        {
                            ticketCount = await ADALottoClient.GetTPTxCountAsync(
                                GameState.StartBlock,
                                GameState.StartBlock + BLOCK_CRAWL_COUNT - 1,
                                GameState.GameGenesisTxMeta.TicketPrice);
                        }
                        GameState.CurrentPot += (long)(ticketCount * GameState.GameGenesisTxMeta.TicketPrice * 0.7);

                        if (GameState.IsDrawing)
                        {
                            nextRoundTicketCount += await ADALottoClient.GetTPTxCountAsync(
                                Math.Max(GameState.StartBlock, GameState.NextDrawBlock),
                                GameState.StartBlock + BLOCK_CRAWL_COUNT - 1,
                                GameState.GameGenesisTxMeta.TicketPrice);

                            Combination = await GetWinningBlocksAsync(GameState.StartBlock, GameState.GameGenesisTxMeta.Digits);

                            if (Combination.Count() == GameState.GameGenesisTxMeta.Digits)
                            {
                                var drawBlockInfo = await ADALottoClient.GetBlockInfo(GameState.NextDrawBlock);
                                if (drawBlockInfo != null)
                                {
                                    var winningTPtxes = await ADALottoClient.GetWinningTPTxesAsync(
                                        GameState.PrevDrawBlock,
                                        GameState.NextDrawBlock - 1,
                                        GameState.GameGenesisTxMeta.TicketPrice,
                                        Combination.Select(wb => int.Parse(wb.Number)));
                                    UpdatePreviousResults(Combination, drawBlockInfo, winningTPtxes.Count());

                                    if (winningTPtxes.Count() > 0)
                                    {
                                        await UpdatePreviousWinnersAsync(winningTPtxes, drawBlockInfo);
                                        GameState.CurrentPot = 0;
                                        GameState.GameGenesisTx = null;
                                        GameState.GameGenesisTxMeta = null;
                                        GameState.NextDrawBlock = 0;
                                        GameState.PrevDrawBlock = 0;
                                    }
                                    else
                                    {
                                        GameState.PrevDrawBlock = GameState.NextDrawBlock;
                                        GameState.NextDrawBlock += GameState.GameGenesisTxMeta.BlockInterval;
                                        GameState.CurrentPot += (long)(nextRoundTicketCount * GameState.GameGenesisTxMeta.TicketPrice * 0.7);
                                        nextRoundTicketCount = 0;
                                    }
                                }
                                GameState.IsDrawing = false;
                                DrawEnd?.Invoke(this, new EventArgs());
                            }
                        }
                    }

                    var ggTx = await ADALottoClient.GetGameGenesisTxAsync(GameState.StartBlock, GameState.StartBlock + BLOCK_CRAWL_COUNT - 1);
                    if (ggTx != null && ggTx?.Block != null)
                    {
                        if (GameState.GameGenesisTx?.Block != null)
                        {
                            GameState.PrevDrawBlock = (long)GameState.GameGenesisTx.Block;
                            GameState.IsDrawing = false;
                            DrawEnd?.Invoke(this, new EventArgs());
                        }
                        else
                        {
                            GameState.PrevDrawBlock = (long)ggTx.Block;
                        }

                        var ggTxMeta = GetMetaFromGGTx(ggTx);
                        if (ggTxMeta != null)
                        {
                            GameState.GameGenesisTx = ggTx;
                            GameState.GameGenesisTxMeta = ggTxMeta;
                            GameState.CurrentPot = ggTxMeta.BasePrize;
                            GameState.NextDrawBlock = (long)ggTx.Block + ggTxMeta.BlockInterval;


                            var ticketCount = await ADALottoClient.GetTPTxCountAsync(
                                (long)GameState.GameGenesisTx.Block,
                                GameState.StartBlock + BLOCK_CRAWL_COUNT - 1,
                                GameState.GameGenesisTxMeta.TicketPrice);
                            GameState.CurrentPot += (long)(ticketCount * GameState.GameGenesisTxMeta.TicketPrice * 0.7);
                        }
                    }
                    OnFetch?.Invoke(this, new EventArgs());
                    GameState.StartBlock = Math.Min(GameState.StartBlock + BLOCK_CRAWL_COUNT, LatestNetworkBlock.Id);
                }
                IsSyncing = false;
                if (!IsInitialSyncFinished)
                {
                    IsInitialSyncFinished = true;
                    InitialSyncComplete?.Invoke(this, new EventArgs());
                }
            }
        }

        public void UpdatePreviousResults(IEnumerable<ALWinningBlock> winningBlocks, Block blockInfo, int winnerCount)
        {
            var result = new ALResult
            {
                DrawDate = blockInfo.Time,
                Numbers = winningBlocks.ToList(),
                Prize = GameState.CurrentPot,
                WinnerCount = winnerCount
            };

            var resultsList = GameState.PreviousResults.ToList();
            resultsList.Insert(0, result);
            GameState.PreviousResults = resultsList.Take(10).ToList();
        }

        public async Task UpdatePreviousWinnersAsync(IEnumerable<Transaction> tpTxes, Block blockInfo)
        {
            foreach (var tpTx in tpTxes)
            {
                if (tpTx.Id != null)
                {
                    GameState.PreviousWinners.ToList().Insert(0, new ALWinner
                    {
                        Address = await ADALottoClient.GetTxSenderAddressAsync((long)tpTx.Id),
                        Prize = GameState.CurrentPot / tpTxes.Count(),
                        DrawBlockId = GameState.NextDrawBlock
                    });
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
                    Number = sizeString.Substring(sizeString.Length - 2)
                };
            })
            .Where(num => num.Number != "00")
            .GroupBy(x => x.Number)
            .Select(x => x.FirstOrDefault())
            .Take(digits)
            .ToList();
        }


        public async Task<long> GetStartBlock()
        {
            GameState.StartBlock = Math.Max(HARD_CHECKPOINT, GameState.StartBlock);
            await GetLatestNetworkBlockAsync();
            var refBlock = LatestNetworkBlock.Id;
            var currentBlock = refBlock;
            var roundCount = 0;

            while (GameState.StartBlock < currentBlock)
            {
                Console.WriteLine(currentBlock);
                var ggTx = await ADALottoClient.GetGameGenesisTxAsync(currentBlock - BLOCK_CRAWL_COUNT - 1, currentBlock);
                if (ggTx != null && ggTx.Block != null)
                {
                    var ggTxMeta = GetMetaFromGGTx(ggTx);
                    if (ggTxMeta != null)
                    {
                        roundCount += (int)(refBlock - ggTx.Block) / ggTxMeta.BlockInterval;
                        refBlock = (long)ggTx.Block;
                    }

                    if (roundCount >= 10)
                    {
                        GameState.StartBlock = (long)ggTx.Block;
                        return GameState.StartBlock;
                    }
                }
                currentBlock -= BLOCK_CRAWL_COUNT;
            }
            return HARD_CHECKPOINT;
        }

        public ALGameGenesisTxMeta? GetMetaFromGGTx(Transaction ggTx)
        {
            if (ggTx?.TxMetadata?.FirstOrDefault()?.Json != null)
                return JsonSerializer.Deserialize<ALGameGenesisTxMeta>(ggTx.TxMetadata.FirstOrDefault().Json);
            else
                return null;
        }

        public static TimeSpan CalculateDrawTime(long startBlockNo, long endBlockNo)
        {
            var timeDiff = (endBlockNo - startBlockNo) * 20;
            return TimeSpan.FromSeconds(timeDiff);
        }
    }
}
