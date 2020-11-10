﻿using ADALotto.Models;
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
        private const long HARD_CHECKPOINT = 4930669;
        private const long BLOCK_CRAWL_COUNT = 599;
        private bool IsSyncing { get; set; } = false;
        private bool IsInitialSyncFinished { get; set; } = false;
        private Block LatestNetworkBlock { get; set; } = new Block();
        private Block PreviousNetworkBlock { get; set; } = new Block();
        public ALGameState GameState { get; set; } = new ALGameState();
        public IEnumerable<ALWinningBlock> Combination { get; set; } = new List<ALWinningBlock>();
        public TimeSpan RemainingRoundTime => GameState.GameGenesisTx != null 
            ? CalculateDrawTime(GameState.StartBlock.BlockNo, GameState.NextDrawBlock.BlockNo) : TimeSpan.FromSeconds(0);

        public double RoundProgress
        {
            get
            {
                var totalRoundTime = CalculateDrawTime(GameState.PrevDrawBlock.BlockNo, GameState.NextDrawBlock.BlockNo);
                var remainingTime = RemainingRoundTime;
                double result = 100 * (totalRoundTime.TotalSeconds - remainingTime.TotalSeconds) / totalRoundTime.TotalSeconds;
                return result;
            }
        }
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
                            GameState.CurrentPot += (long)(ticketCount * GameState.GameGenesisTxMeta.TicketPrice * 0.7);

                            if (GameState.IsDrawing)
                            {
                                var startBlock = await ADALottoClient.GetBlockInfo(Math.Max(GameState.StartBlock.BlockNo, GameState.NextDrawBlock.BlockNo));
                                var endBlock = await ADALottoClient.GetBlockInfo(Math.Min(GameState.StartBlock.BlockNo + BLOCK_CRAWL_COUNT, LatestNetworkBlock.BlockNo) - 1);
                                nextRoundTicketCount += await ADALottoClient.GetTPTxCountAsync(
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
                                        UpdatePreviousResults(Combination, drawBlockInfo, winningTPtxes.Count());

                                        if (winningTPtxes.Count() > 0)
                                        {
                                            await UpdatePreviousWinnersAsync(winningTPtxes);
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
                                            GameState.CurrentPot += (long)(nextRoundTicketCount * GameState.GameGenesisTxMeta.TicketPrice * 0.7);
                                            nextRoundTicketCount = 0;
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
                                GameState.CurrentPot += (long)(ticketCount * GameState.GameGenesisTxMeta.TicketPrice * 0.7);
                            }
                        }

                        GameState.StartBlock = LatestNetworkBlock.BlockNo <= GameState.StartBlock.BlockNo + BLOCK_CRAWL_COUNT
                            ? LatestNetworkBlock : await ADALottoClient.GetBlockInfo(GameState.StartBlock.BlockNo + BLOCK_CRAWL_COUNT);

                        Console.WriteLine(GameState.StartBlock.BlockNo);
                        OnFetch?.Invoke(this, new EventArgs());
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

        private async Task UpdatePreviousWinnersAsync(IEnumerable<Transaction> tpTxes)
        {
            foreach (var tpTx in tpTxes)
            {
                if (tpTx.Id != null)
                {
                    var newWinner = new ALWinner
                    {
                        Address = await ADALottoClient.GetTxSenderAddressAsync((long)tpTx.Id),
                        Prize = GameState.CurrentPot / tpTxes.Count(),
                        DrawBlock = GameState.NextDrawBlock
                    };
                    
                    var winnerList = GameState.PreviousWinners.ToList() ?? new List<ALWinner>();
                    winnerList.Insert(0, newWinner);
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

        public static TimeSpan CalculateDrawTime(long startBlockNo, long endBlockNo)
        {
            var timeDiff = (endBlockNo - startBlockNo) * 20;
            return TimeSpan.FromSeconds(timeDiff);
        }
    }
}
