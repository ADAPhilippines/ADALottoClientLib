using ADALotto.Models;
using ADALottoModels;
using ADALottoModels.Enumerations;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ADALotto.Client
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

        public async Task<Transaction?> GetRewardTxAsync(float startBlock, float endBlock, float amount, string address)
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

        public async Task<Transaction?> GetGameGenesisTxAsync(float startBlock, float endBlock)
        {
            var transactions = await GetGameTransactionsAsync(GameTxMetaType.Genesis, startBlock, endBlock, GameWalletAddress, GameWalletAddress, null, "DESC");
            return transactions?.FirstOrDefault();
        }

        public async Task<Transaction?> GetEndGameTxAsync(float startBlock, float endBlock)
        {
            var transactions = await GetGameTransactionsAsync(GameTxMetaType.EndGame, startBlock, endBlock, GameWalletAddress, GameWalletAddress, null, "DESC");
            return transactions?.FirstOrDefault();
        }

        public async Task<IEnumerable<Block>> GetWinningBlocksAsync(float startBlock)
        {
            var query = new GraphQLRequest
            {
                Query = $@"
                        query {{
                          blockChainInfo {{
                            blocks (first: 50, where: {{ id_gt: { startBlock }, txCount_gt: 0 }}) {{
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

        public async Task<Block?> GetBlockInfo(float id)
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

        public async Task<IEnumerable<Transaction>?> GetTicketPurchaseTxAsync(float startBlock, float endBlock, float amount)
        {
            var transactions = await GetGameTransactionsAsync(GameTxMetaType.TicketPurchase, startBlock, endBlock, GameWalletAddress, null, null, "ASC", amount);
            //if (startBlock > 4923590 && endBlock < 4923609)
            //{
            //    var txMeta = new ADALottoGameTicketTxMeta();
            //    txMeta.Combination = new List<int> { 61, 83, 16 };
            //    var newTx = new Transaction
            //    {
            //        Id = 2939174,
            //        Block = 4923599,
            //        TxMetadata = new List<TransactionMeta>
            //    {
            //        new TransactionMeta { Id = 12345566, Json = JsonSerializer.Serialize(txMeta)}
            //    }
            //    };
            //    if (transactions == null)
            //        transactions = new List<Transaction>();
            //    transactions = transactions.ToList();
            //    ((List<Transaction>)transactions).Add(newTx);
            //}

            return transactions;
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

        public async Task<IEnumerable<Transaction>> GetGameTransactionsAsync(GameTxMetaType type, float startBlock, float endBlock, string receiver, string? sender = "", int? limit=null, string sortDir = "ASC", float amount = 1000000)
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

        public async Task<string?> GetTxSenderAddressAsync(float txId)
        {
            var result = string.Empty;
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
