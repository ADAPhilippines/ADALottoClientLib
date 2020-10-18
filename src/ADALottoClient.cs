using ADALotto.Models;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using System.Collections.Generic;
using System.Linq;
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

        public async Task<Transaction?> GetGameGenesisTxAsync(float startBlock, float endBlock)
        {
            var transactions = await GetGameTransactionsAsync(0, startBlock, endBlock, GameWalletAddress, GameWalletAddress);

            return transactions?.FirstOrDefault();
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
                                blocks (order_by: {{ id: DESC }}, first: { limit }) {{
                                    nodes {{
                                        id,
                                        epochNo
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

        public async Task<IEnumerable<Transaction>> GetGameTransactionsAsync(int type, float startBlock, float endBlock, string receiver, string sender = "", int limit=10, string sortDir = "ASC")
        {
            var result = new List<Transaction>();
            if (sortDir == "ASC" || sortDir == "DESC")
            {
                var query = new GraphQLRequest
                {
                    Query = $@"
                    query ($filter: AdaLottoTxFilterInput!) {{
                        adaLottoGameInfo {{
                            transactions(filter: $filter, first: { limit }, where: {{ AND: [{{ block_gte: { startBlock } }}, {{ block_lte: { endBlock } }}] }}) {{
                                nodes {{
                                    id,
                                    block,
                                    txMetadata {{
                                        id
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
                            sender = sender,
                            receiver = receiver,
                            type = type
                        }
                    }
                };
                var graphQLResponse = await GraphQLClient.SendQueryAsync<QueryResponse>(query);

                if (graphQLResponse?.Data?.AdaLottoGameInfo?.Transactions != null)
                    result = graphQLResponse.Data.AdaLottoGameInfo.Transactions.Nodes.ToList();
            }

            return result;
        }
    }
}
