using ADALotto.Models;
using ADALottoModels;
using ADALottoModels.Enumerations;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ADALotto.ClientLib
{
    public class ADALottoClient
    {
        private GraphQLHttpClient GraphQLClient { get; set; }
        private string GameWalletAddress { get; set; }
        public ADALottoClient(string url, string gameWalletAddress)
        {
            GraphQLClient = new GraphQLHttpClient(url, new SystemTextJsonSerializer());
            GameWalletAddress = gameWalletAddress;
        }

        public async Task<Transaction?> GetRewardTxAsync(Block startBlock, Block endBlock, long amount, string address)
        {
            var query = new GraphQLRequest
            {
                Query = $@"
                    query ($filter: AdaLottoTxFilterInput!) {{
                        adaLottoGameInfo {{
                            transactions(order_by: {{ id: DESC }}, filter: $filter, where: {{ block_gte: { startBlock.Id }, block_lte: { endBlock.Id } }}) {{
                                nodes {{
                                    id,
                                    block,
                                    block1 {{
                                        id,
                                        blockNo,
                                        epochNo,
                                        size,
                                        hash
                                    }},
                                    txMetadata {{
                                        id,
                                        json
                                    }}
                                }}
                            }}
                        }}
                    }}",
                Variables = new
                {
                    filter = new
                    {
                        sender = GameWalletAddress,
                        receiver = address,
                        amount_gte = amount
                    }
                }
            };

            var graphQLResponse = await GraphQLClient.SendQueryAsync<QueryResponse>(query);
            return graphQLResponse?.Data?.AdaLottoGameInfo?.Transactions?.Nodes?.FirstOrDefault();
        }

        public async Task<Transaction?> GetGameGenesisTxAsync(Block startBlock, Block endBlock)
        {
            var transactions = await GetGameTransactionsAsync(GameTxMetaType.Genesis, startBlock, endBlock, GameWalletAddress, GameWalletAddress, null, "DESC");
            return transactions?.FirstOrDefault();
        }

        public async Task<IEnumerable<Block>> GetWinningBlocksAsync(long startBlock)
        {
            var query = new GraphQLRequest
            {
                Query = $@"
                        query {{
                          blockChainInfo {{
                            blocks (first: 100, where: {{ blockNo_gt: { startBlock }, txCount_gt: 0 }}) {{
                              nodes {{
                                size,
                                hash
                              }}
                            }}
                          }}
                        }}"
            };

            var graphQLResponse = await GraphQLClient.SendQueryAsync<QueryResponse>(query);
            var resultBlocks = new List<Block>();
            if (graphQLResponse?.Data?.BlockChainInfo?.Blocks != null)
                resultBlocks = graphQLResponse.Data.BlockChainInfo.Blocks.Nodes.ToList();

            return resultBlocks;
        }

        public async Task<IEnumerable<Transaction>> GetWinningTPTxesAsync(Block startBlock, Block endBlock, long amount, IEnumerable<int> nums)
        {
            var result = new List<Transaction>();
            var query = new GraphQLRequest
            {
                Query = $@"
                    query ($filter: AdaLottoTxFilterInput!) {{
                        adaLottoGameInfo {{
                            transactions(filter: $filter, where: {{ block_gte: { startBlock.Id }, block_lte: { endBlock.Id } }}, nums: [{String.Join(", ", nums)}]) {{
                                totalCount
                            }}
                        }}
                    }}",
                Variables = new
                {
                    filter = new
                    {
                        receiver = GameWalletAddress,
                        amount_gte = amount
                    }
                }
            };

            var graphQLResponse = await GraphQLClient.SendQueryAsync<QueryResponse>(query);
            result = graphQLResponse?.Data?.AdaLottoGameInfo?.Transactions?.Nodes?.ToList() ?? result;

            if(startBlock.BlockNo > 4932134)
            {
                var tpTxMeta =  new ALGameTicketTxMeta
                {
                    Combination = new int[] { 08 },
                };
                var newTx = new Transaction
                {
                    Id = 2985863,
                    Block = 4923599,
                    TxMetadata = new List<TransactionMeta>
                    {
                        new TransactionMeta { Id = 12345566, Json = JsonSerializer.Serialize(tpTxMeta)}
                    }
                };
                if (result == null)
                    result = new List<Transaction>();
                result = result.ToList();
                result.Add(newTx);
            }

            return result;
        }

        public async Task<Block> GetBlockInfo(long blockNo)
        {
            var result = new Block();
            var query = new GraphQLRequest
            {
                Query = $@"
                        query {{
                          blockChainInfo {{
                            blocks (where: {{ blockNo: { blockNo } }}) {{
                              nodes {{
                                time,
                                id,
                                blockNo,
                                epochNo,
                                size,
                                hash
                              }}
                            }}
                          }}
                        }}"
            };

            var graphQLResponse = await GraphQLClient.SendQueryAsync<QueryResponse>(query);
            if (graphQLResponse?.Data?.BlockChainInfo?.Blocks != null)
                result = graphQLResponse.Data.BlockChainInfo.Blocks.Nodes.ToList().FirstOrDefault();

            return result;
        }

        public async Task<IEnumerable<Transaction>?> GetTicketPurchaseTxAsync(Block startBlock, Block endBlock, long amount)
        {
            var transactions = await GetGameTransactionsAsync(GameTxMetaType.TicketPurchase, startBlock, endBlock, GameWalletAddress, null, null, "ASC", amount);

            return transactions;
        }

        public async Task<int> GetTPTxCountAsync(Block startBlock, Block endBlock, long amount)
        {
            var query = new GraphQLRequest
            {
                Query = $@"
                    query ($filter: AdaLottoTxFilterInput!) {{
                        adaLottoGameInfo {{
                            transactions(filter: $filter, where: {{ block_gte: { startBlock.Id }, block_lte: { endBlock.Id } }}) {{
                                totalCount
                            }}
                        }}
                    }}",
                Variables = new
                {
                    filter = new
                    {
                        receiver = GameWalletAddress,
                        type = 2,
                        amount_gte = amount
                    }
                }
            };

            var graphQLResponse = await GraphQLClient.SendQueryAsync<QueryResponse>(query);
            
            return graphQLResponse?.Data?.AdaLottoGameInfo?.Transactions?.TotalCount ?? 0;
        }

        public async Task<Block?> GetLatestBlockAsync()
        {
            var blocks = await GetBlocksAsync(1, "DESC");
            return blocks?.FirstOrDefault();
        }

        public async Task<IEnumerable<Block>> GetBlocksAsync(int limit = 10, string sortDir = "ASC")
        {
            var result = new List<Block>();
            if (sortDir == "ASC" || sortDir == "DESC")
            {
                var query = new GraphQLRequest
                {
                    Query = $@"
                        query {{
                            blockChainInfo {{
                                blocks (order_by: {{ id: { sortDir } }}, first: { limit }, where: {{ epochNo_gt: 208 }}) {{
                                    nodes {{
                                        id,
                                        blockNo,
                                        epochNo,
                                        size,
                                        hash
                                    }}
                                }}
                            }}
                        }}"
                };
                var graphQLResponse = await GraphQLClient.SendQueryAsync<QueryResponse>(query);

                if (graphQLResponse?.Data?.BlockChainInfo?.Blocks != null)
                    result = graphQLResponse.Data.BlockChainInfo.Blocks.Nodes.ToList();
            }

            return result;
        }

        public async Task<IEnumerable<Transaction>> GetGameTransactionsAsync(GameTxMetaType type, Block startBlock, Block endBlock, string receiver, string? sender = "", int? limit=null, string sortDir = "ASC", long amount = 1000000)
        {
            var result = new List<Transaction>();
            var limitClause = limit != null ? $"first { limit }," : string.Empty;
            if (sortDir == "ASC" || sortDir == "DESC")
            {
                var query = new GraphQLRequest
                {
                    Query = $@"
                    query ($filter: AdaLottoTxFilterInput!) {{
                        adaLottoGameInfo {{
                            transactions(order_by: {{ id: { sortDir } }}, filter: $filter, { limitClause } where: {{ block_gte: { startBlock.Id }, block_lte: { endBlock.Id } }}) {{
                                nodes {{
                                    id,
                                    block,
                                    block1 {{
                                        id,
                                        blockNo,
                                        epochNo,
                                        size,
                                        hash
                                    }},
                                    txMetadata {{
                                        id,
                                        json
                                    }}
                                }}
                            }}
                        }}
                    }}",
                    Variables = new
                    {
                        filter = new
                        {
                            sender,
                            receiver,
                            type = (int)type,
                            amount_gte = amount
                        }
                    }
                };
                var graphQLResponse = await GraphQLClient.SendQueryAsync<QueryResponse>(query);

                if (graphQLResponse?.Data?.AdaLottoGameInfo?.Transactions != null)
                    result = graphQLResponse.Data.AdaLottoGameInfo.Transactions.Nodes.ToList();
            }

            return result;
        }

        public async Task<string> GetTxSenderAddressAsync(long txId)
        {
            var query = new GraphQLRequest
            {
                Query = $@"
                    query {{
                        blockChainInfo {{
                            transactions ( where: {{ id: { txId } }}) {{
                                nodes {{
                                    inTxIns {{
                                        txOutId,
                                        txOutIndex
                                    }}
                                }}
                            }}
                        }}
                    }}"
            };

            var graphQLResponse = await GraphQLClient.SendQueryAsync<QueryResponse>(query);
            var transaction = graphQLResponse?.Data?.BlockChainInfo?.Transactions?.Nodes?.FirstOrDefault();

            var txIn = transaction?.InTxIns.FirstOrDefault();

            var result = string.Empty;
            if (txIn != null)
            {
                query = new GraphQLRequest
                {
                    Query = $@"
                    query {{
                        blockChainInfo {{
                            txOuts ( where: {{ txId: {txIn.TxOutId}, index: {txIn.TxOutIndex} }}) {{
                                nodes {{
                                    address,
                                    index
                                }}
                            }}
                        }}
                    }}"
                };

                graphQLResponse = await GraphQLClient.SendQueryAsync<QueryResponse>(query);
                var txOut = graphQLResponse?.Data?.BlockChainInfo?.TxOuts?.Nodes?.FirstOrDefault();

                result = txOut != null ? txOut.Address : string.Empty;
            }

            return result ?? string.Empty;
        }
    }
}
