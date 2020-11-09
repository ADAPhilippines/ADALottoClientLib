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

        public async Task<Transaction?> GetRewardTxAsync(long startBlock, long endBlock, long amount, string address)
        {
            var query = new GraphQLRequest
            {
                Query = $@"
                    query ($filter: AdaLottoTxFilterInput!) {{
                        adaLottoGameInfo {{
                            transactions(order_by: {{ id: DESC }}, filter: $filter, where: {{ block_gte: { startBlock }, block_lte: {endBlock} }}) {{
                                nodes {{
                                    id,
                                    block,
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

        public async Task<Transaction?> GetGameGenesisTxAsync(long startBlock, long endBlock)
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
                            blocks (first: 100, where: {{ id_gt: { startBlock }, txCount_gt: 0 }}) {{
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

        public async Task<IEnumerable<Transaction>> GetWinningTPTxesAsync(long startBlock, long endBlock, long amount, IEnumerable<int> nums)
        {
            var result = new List<Transaction>();
            var query = new GraphQLRequest
            {
                Query = $@"
                    query ($filter: AdaLottoTxFilterInput!) {{
                        adaLottoGameInfo {{
                            transactions(filter: $filter, where: {{ block_gte: { startBlock }, block_lte: {endBlock} }}, nums: [{String.Join(", ", nums)}]) {{
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

            return result;
        }

        public async Task<Block?> GetBlockInfo(long id)
        {
            Block? result;
            var query = new GraphQLRequest
            {
                Query = $@"
                        query {{
                          blockChainInfo {{
                            blocks (where: {{ id: { id } }}) {{
                              nodes {{
                                time,
                                id,
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
            else
                result = null;

            return result;
        }

        public async Task<IEnumerable<Transaction>?> GetTicketPurchaseTxAsync(long startBlock, long endBlock, long amount)
        {
            var transactions = await GetGameTransactionsAsync(GameTxMetaType.TicketPurchase, startBlock, endBlock, GameWalletAddress, null, null, "ASC", amount);

            return transactions;
        }

        public async Task<int> GetTPTxCountAsync(long startBlock, long endBlock, long amount)
        {
            var query = new GraphQLRequest
            {
                Query = $@"
                    query ($filter: AdaLottoTxFilterInput!) {{
                        adaLottoGameInfo {{
                            transactions(filter: $filter, where: {{ block_gte: { startBlock }, block_lte: {endBlock} }}) {{
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

        public async Task<IEnumerable<Transaction>> GetGameTransactionsAsync(GameTxMetaType type, long startBlock, long endBlock, string receiver, string? sender = "", int? limit=null, string sortDir = "ASC", long amount = 1000000)
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
                            transactions(order_by: {{ id: { sortDir } }}, filter: $filter, { limitClause } where: {{ block_gte: { startBlock }, block_lte: {endBlock} }}) {{
                                nodes {{
                                    id,
                                    block,
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

            return result;
        }
    }
}
