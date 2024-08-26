using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Neo4jClient;
using static CollectorApi.Controllers.Collector.SnmpController;

namespace CollectorApi.Tool
{
    public class ApiTool
    {
        public static ConcurrentQueue<SnmpTaskInfo> snmpResQueue = new ConcurrentQueue<SnmpTaskInfo>();
        private string neu4jUri = "neo4j://neo4j.dev.hannlync.com:7687";
        private string neu4jUser = "neo4j";
        private string neu4jPwd = "Neo4jCefinity";

        public async Task<List<ResSysCode>> FuncGetSysCode(ReqSysCode request)
        {
            List<ResSysCode> dataList = new List<ResSysCode>();

            try
            {
                using (var client = new BoltGraphClient(neu4jUri, neu4jUser, neu4jPwd))
                {
                    await client.ConnectAsync().ConfigureAwait(false);

                    var query = client.Cypher.Match("(syscode:syscode)");

                    if (!string.IsNullOrEmpty(request.codeGroup))
                    {
                        query = query.Where("(syscode.codeGroup = $codeGroup)")
                                     .WithParam("codeGroup", request.codeGroup);
                    }

                    query = query.AndWhere("syscode.status = 80");

                    var sysCodeList = await query.Return((syscode) => new ResSysCode
                    {
                        syscodeId = syscode.As<ResSysCode>().syscodeId,
                        codeGroup = syscode.As<ResSysCode>().codeGroup,
                        codeName = syscode.As<ResSysCode>().codeName,
                        codeNo = syscode.As<ResSysCode>().codeNo
                    }).ResultsAsync.ConfigureAwait(false);

                    dataList = sysCodeList.ToList();
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            return dataList;
        }

        public class ReqSysCode
        {
            public string? codeGroup { get; set; }
        }
        public class ResSysCode
        {
            [Newtonsoft.Json.JsonProperty(PropertyName = "id")]
            public string? syscodeId { get; set; }
            public string? codeGroup { get; set; }
            public string? codeName { get; set; }
            public int? codeNo { get; set; }
        }
    }
}