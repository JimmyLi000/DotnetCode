using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CollectorApi.Tool;
using Dapper;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Neo4jClient;
using Newtonsoft.Json;
using Npgsql;

namespace CollectorApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("CorsPolicy")]
    public class AeonMatrixController : BaseApiController
    {
        private readonly IConfiguration _configuration;
        private string? neu4jUri;
        private string? neu4jUser;
        private string? neu4jPwd;
        private string? PostgreDbNetmatrix;

        public AeonMatrixController(IConfiguration configuration)
        {
            _configuration = configuration;

            var neo4jDBSetting = _configuration.GetSection("neo4jDB");
            neu4jUri = neo4jDBSetting["Url"];
            neu4jUser = neo4jDBSetting["Account"];
            neu4jPwd = neo4jDBSetting["Password"];

            var postgreDBSetting = _configuration.GetSection("PostgreDB");
            PostgreDbNetmatrix = postgreDBSetting["Netmatrix"];
        }

        [HttpPost("AddOrUpdWfaConfig")]
        public async Task<object> AddOrUpdWfaConfig(List<ReqWfaConfig> requestList)
        {
            List<string> errorParamList = new List<string>();

            try
            {
                foreach (var request in requestList)
                {
                    request.processor_person_id = request.upd_id;

                    if (string.IsNullOrEmpty(request.name))
                    {
                        errorParamList.Add("name");
                    }
                    if (!request.status.HasValue)
                    {
                        errorParamList.Add("status");
                    }
                }
                if (errorParamList.Count > 0)
                {
                    throw new Exception("Request param error.");
                }

                using (var conn = new NpgsqlConnection(PostgreDbNetmatrix))
                {
                    await conn.OpenAsync().ConfigureAwait(false);

                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            foreach (var request in requestList)
                            {
                                var WfaConfigSql = request.wfa_config_no.HasValue ?
                                            $@"UPDATE wfa_config SET 
                                              person_id = @person_id, 
                                              name = @name, 
                                              field_id = @field_id, 
                                              processor_person_id = @processor_person_id, 
                                              asset_id = @asset_id,
                                              asset_ad_id = @asset_ad_id,
                                              asset_person_no = @asset_person_no,
                                              interconnect_no = @interconnect_no,
                                              device_uuid = @device_uuid,
                                              status = @status, 
                                              start_date = @start_date, 
                                              end_date = @end_date, 
                                              upd_id = @upd_id, 
                                              upd_date = now() 
                                        WHERE wfa_config_no = @wfa_config_no
                                        RETURNING wfa_config_no"
                                                                            :
                                            $@"INSERT INTO wfa_config 
                                              (name, person_id, field_id, processor_person_id, device_uuid, asset_id, asset_ad_id, asset_person_no, interconnect_no, status, start_date, end_date, crt_id, crt_date, upd_id, upd_date) 
                                              VALUES 
                                              (@name, @person_id, @field_id, @processor_person_id, @device_uuid, @asset_id, @asset_ad_id, @asset_person_no, @interconnect_no, @status, @start_date, @end_date, @crt_id, now(), @upd_id, now())
                                              RETURNING wfa_config_no";

                                var wfaConfigNo = await conn.ExecuteScalarAsync<int>(WfaConfigSql, request).ConfigureAwait(false);

                                var WfaConfigLogSql = $@"INSERT INTO wfa_config_log 
                                              (wfa_config_no, tdate, name, field_id, device_uuid, asset_id, asset_ad_id, asset_person_no, description, wfa_status, status, crt_id, crt_date, upd_id, upd_date) 
                                              VALUES 
                                              (@wfa_config_no, now(), @name, @field_id, @device_uuid, @asset_id, @asset_ad_id, @asset_person_no, @description, @wfa_status, @status, @crt_id, now(), @upd_id, now())";

                                var WfaConfigLogNum = await conn.ExecuteAsync(WfaConfigLogSql,
                                                                new
                                                                {
                                                                    wfa_config_no = wfaConfigNo,
                                                                    name = request.name,
                                                                    field_id = request.field_id,
                                                                    device_uuid = request.device_uuid,
                                                                    asset_id = request.asset_id,
                                                                    asset_ad_id = request.asset_ad_id,
                                                                    asset_person_no = request.asset_person_no,
                                                                    description = request.description,
                                                                    status = 80,
                                                                    wfa_status = request.status,
                                                                    crt_id = request.crt_id,
                                                                    upd_id = request.upd_id
                                                                });
                            }

                            await transaction.CommitAsync().ConfigureAwait(false);
                        }
                        catch
                        {
                            await transaction.RollbackAsync().ConfigureAwait(false);
                            throw;
                        }
                    }
                }

                apiResult.RetCode = 200;
                apiResult.RetMsg = "Success";
            }
            catch (Exception ex)
            {
                if (errorParamList.Count > 0)
                {
                    apiResult.RetMsg = $"The {string.Join(",", errorParamList)} is required.";
                }
                else
                {
                    apiResult.RetMsg = ex.ToString();
                }

                apiResult.RetCode = -1;
                logger.Error(ex);
            }
            finally
            {
                AfterProcess();
            }

            return apiResult;
        }

        [HttpPost("GetWfaConfig")]
        public async Task<object> GetWfaConfig(ReqGetWfaConfig request)
        {
            try
            {
                string sqlParam = string.Empty;
                if (request.wfa_config_no.HasValue)
                {
                    sqlParam += " AND wc.wfa_config_no = @wfa_config_no ";
                }
                if (!string.IsNullOrEmpty(request.person_id))
                {
                    sqlParam += " AND wc.person_id = @person_id ";
                }
                if (request.asset_person_no.HasValue)
                {
                    sqlParam += " AND wc.asset_person_no = @asset_person_no ";
                }
                if (request.network_element_no.HasValue)
                {
                    sqlParam += " AND ne.network_element_no = @network_element_no ";
                }
                if (request.asset_field_config_no.HasValue)
                {
                    sqlParam += " AND afc.asset_field_config_no = @asset_field_config_no ";
                }
                if (request.start_status.HasValue && request.end_status.HasValue)
                {
                    sqlParam += " AND wc.status BETWEEN @start_status AND @end_status ";
                }
                else
                {
                    sqlParam += " AND wc.status NOT BETWEEN 500 AND 599 ";
                }

                string pageSql = string.Empty;
                if (request.pageNumber.HasValue && request.pageSize.HasValue)
                {
                    pageSql = " LIMIT @pageSize OFFSET (@pageNumber - 1) * @pageSize ";
                }

                using (var conn = new NpgsqlConnection(PostgreDbNetmatrix))
                {
                    await conn.OpenAsync().ConfigureAwait(false);

                    var countSql = $@" SELECT COUNT(1)
                                        FROM wfa_config wc
                                            LEFT JOIN ip_address ia
                                                        INNER JOIN network_element ne 
                                                                ON ne.ip_address_no = ia.ip_address_no  
                                                                AND ne.status = 80
                                                    ON ne.asset_id = wc.asset_id 
                                                    AND ne.status = 80
                                            LEFT JOIN asset_person ap 
                                                    ON ap.asset_person_no = wc.asset_person_no
                                                    AND ap.status = 80
                                            LEFT JOIN asset_field_config afc 
                                                    ON afc.asset_id = wc.asset_id 
                                                    AND afc.status = 80
                                        WHERE 1 = 1 
                                        AND wc.status < 950
                                            {sqlParam} ";

                    int totalRecords = await conn.ExecuteScalarAsync<int>(countSql, request).ConfigureAwait(false);

                    var sql = $@" SELECT wc.wfa_config_no, wc.person_id, wc.name, wc.field_id
                                            , wc.processor_person_id, wc.device_uuid, wc.asset_id, wc.asset_person_no, ap.name AS asset_person_name
                                            , wc.status, wc.start_date, wc.end_date, wc.asset_ad_id, wc.connect_type, wc.account, wc.password
                                            , wc.interconnect_no, ic.name AS interconnect_name, ic.source_asset_id, ic.destination_asset_id, ic.connect_type
                                            , wc.crt_id, wc.crt_date, wc.upd_id, wc.upd_date
                                            , ia.ip_address_no, ia.name AS ip_address_name, ia.ip_address, ia.mask AS ip_address_mask, ia.dns AS ip_address_dns
                                            , ia.dhcp_start AS ip_address_dhcp_start, ia.dhcp_end AS ip_address_dhcp_end, ia.gateway AS ip_address_gateway
                                            , ne.network_element_no, ne.name AS network_element_name, afc.asset_field_config_no, afc.name AS asset_field_config_name
                                    FROM wfa_config wc
                                        LEFT JOIN ip_address ia
                                                    INNER JOIN network_element ne 
                                                            ON ne.ip_address_no = ia.ip_address_no  
                                                            AND ne.status = 80
                                                ON ne.asset_id = wc.asset_id 
                                                AND ne.status = 80
                                        LEFT JOIN asset_person ap 
                                                ON ap.asset_person_no = wc.asset_person_no
                                                AND ap.status = 80
                                        LEFT JOIN asset_field_config afc 
                                                ON afc.asset_id = wc.asset_id 
                                                AND afc.status = 80
                                        LEFT JOIN interconnect ic
                                        	   ON ic.interconnect_no = wc.interconnect_no 
                                        	  AND ic.status = 80
                                    WHERE 1 = 1 
                                      AND wc.status < 950
                                        {sqlParam}
                                    ORDER BY (CASE WHEN wc.status BETWEEN 200 AND 599 THEN 99999 ELSE wc.status END) DESC, wc.wfa_config_no DESC
                                        {pageSql} ";

                    Tool.ApiTool apiTool = new Tool.ApiTool();
                    var wfaStatusTask = apiTool.FuncGetSysCode(new Tool.ApiTool.ReqSysCode() { codeGroup = "wfaStatus" });
                    var assetConnectTypeTask = apiTool.FuncGetSysCode(new Tool.ApiTool.ReqSysCode() { codeGroup = "assetConnectType" });

                    var resultsData = (await conn.QueryAsync<dynamic>(sql, request).ConfigureAwait(false)).ToList();

                    // 從 neo4j 取得關聯資料
                    ConcurrentDictionary<string, Task<ApiBaseModel>> taskList = new ConcurrentDictionary<string, Task<ApiBaseModel>>();
                    List<string> assetIdList = new List<string>();
                    List<string> personIdList = new List<string>();
                    List<string> fieldIdList = new List<string>();

                    foreach (var row in resultsData)
                    {
                        if (row.asset_id != null)
                        {
                            assetIdList.Add(row.asset_id);
                        }
                        if (row.asset_ad_id != null)
                        {
                            assetIdList.Add(row.asset_ad_id);
                        }
                        if (row.source_asset_id != null)
                        {
                            assetIdList.Add(row.source_asset_id);
                        }
                        if (row.destination_asset_id != null)
                        {
                            assetIdList.Add(row.destination_asset_id);
                        }
                        if (row.person_id != null)
                        {
                            personIdList.Add(row.person_id);
                        }
                        if (row.processor_person_id != null)
                        {
                            personIdList.Add(row.processor_person_id);
                        }
                        if (row.field_id != null)
                        {
                            fieldIdList.Add(row.field_id);
                        }
                    }

                    if (assetIdList.Count > 0)
                    {
                        NetMatrixController netMatrixController = new NetMatrixController(_configuration);
                        taskList["assetList"] = netMatrixController.GetAsset(new() { assetIdList = assetIdList });
                    }
                    if (personIdList.Count > 0)
                    {
                        CommonController commonController = new CommonController(_configuration);
                        taskList["personList"] = commonController.GetPerson(new() { personIdList = personIdList });
                    }
                    if (fieldIdList.Count > 0)
                    {
                        NetMatrixController netMatrixController = new NetMatrixController(_configuration);
                        taskList["fieldList"] = netMatrixController.GetField(new() { fieldIdList = fieldIdList });
                    }

                    Task t = Task.WhenAll(taskList.Values);
                    await t.ConfigureAwait(false);

                    var getAssetRes = taskList.ContainsKey("assetList") ? await taskList["assetList"].ConfigureAwait(false) : null;
                    var getPersonRes = taskList.ContainsKey("personList") ? await taskList["personList"].ConfigureAwait(false) : null;
                    var getFieldRes = taskList.ContainsKey("fieldList") ? await taskList["fieldList"].ConfigureAwait(false) : null;
                    var wfaStatusList = await wfaStatusTask.ConfigureAwait(false);
                    var assetConnectTypeList = await assetConnectTypeTask.ConfigureAwait(false);

                    List<dynamic> assetList = new List<dynamic>();
                    List<dynamic> personList = new List<dynamic>();
                    List<dynamic> fieldList = new List<dynamic>();

                    if (getAssetRes != null && getAssetRes.RetCode == 200 && getAssetRes.ResultSet != null)
                    {
                        var dataList = JsonConvert.DeserializeObject<List<dynamic>>(JsonConvert.SerializeObject(getAssetRes.ResultSet));
                        assetList.AddRange(dataList);
                    }
                    if (getPersonRes != null && getPersonRes.RetCode == 200 && getPersonRes.ResultSet != null)
                    {
                        var dataList = JsonConvert.DeserializeObject<List<dynamic>>(JsonConvert.SerializeObject(getPersonRes.ResultSet));
                        personList.AddRange(dataList);
                    }
                    if (getFieldRes != null && getFieldRes.RetCode == 200 && getFieldRes.ResultSet != null)
                    {
                        var dataList = JsonConvert.DeserializeObject<List<dynamic>>(JsonConvert.SerializeObject(getFieldRes.ResultSet));
                        fieldList.AddRange(dataList);
                    }

                    foreach (var row in resultsData)
                    {
                        {
                            var dataIndex = assetList.FindIndex(x => x.assetId == row.asset_id);
                            row.asset_name = dataIndex != -1 ? (string?)(assetList[dataIndex].name) : null;
                            row.attributes = dataIndex != -1 ? tool.DeserializeJson(assetList[dataIndex].attributes) : null;
                        }
                        {
                            var dataIndex = assetList.FindIndex(x => x.assetId == row.asset_ad_id);
                            row.asset_ad_name = dataIndex != -1 ? (string?)(assetList[dataIndex].name) : null;
                            row.asset_ad_attributes = dataIndex != -1 ? tool.DeserializeJson(assetList[dataIndex].attributes) : null;
                        }
                        {
                            var dataIndex = assetList.FindIndex(x => x.assetId == row.source_asset_id);
                            row.source_asset_name = dataIndex != -1 ? (string?)(assetList[dataIndex].name) : null;
                            row.source_asset_attributes = dataIndex != -1 ? tool.DeserializeJson(assetList[dataIndex].attributes) : null;
                        }
                        {
                            var dataIndex = assetList.FindIndex(x => x.assetId == row.destination_asset_id);
                            row.destination_asset_name = dataIndex != -1 ? (string?)(assetList[dataIndex].name) : null;
                            row.destination_asset_attributes = dataIndex != -1 ? tool.DeserializeJson(assetList[dataIndex].attributes) : null;
                        }
                        {
                            var dataIndex = personList.FindIndex(x => x.personId == row.person_id);
                            row.person_name = dataIndex != -1 ? (string?)(personList[dataIndex].name) : null;
                        }
                        {
                            var dataIndex = personList.FindIndex(x => x.personId == row.processor_person_id);
                            row.processor_person_name = dataIndex != -1 ? (string?)(personList[dataIndex].name) : null;
                        }
                        {
                            var dataIndex = fieldList.FindIndex(x => x.fieldId == row.field_id);
                            row.field_name = dataIndex != -1 ? (string?)(fieldList[dataIndex].name) : null;
                        }
                        {
                            var dataIndex = wfaStatusList.FindIndex(x => x.codeNo == row.status);
                            row.statusName = dataIndex != -1 ? wfaStatusList[dataIndex].codeName : null;
                        }
                        {
                            var dataIndex = assetConnectTypeList.FindIndex(x => x.codeNo == row.connect_type);
                            row.connect_type_name = dataIndex != -1 ? assetConnectTypeList[dataIndex].codeName : null;
                        }
                    }

                    apiResult.RetNumber = totalRecords;
                    apiResult.ResultSet = resultsData;
                }

                apiResult.RetCode = 200;
                apiResult.RetMsg = "Success";
            }
            catch (Exception ex)
            {
                apiResult.RetMsg = ex.ToString();
                apiResult.RetCode = -1;
                logger.Error(ex);
            }
            finally
            {
                AfterProcess();
            }

            return apiResult;
        }

        [HttpPost("GetWfaConfigLog")]
        public async Task<object> GetWfaConfigLog(ReqGetWfaConfigLog request)
        {
            try
            {
                string sqlParam = string.Empty;
                if (request.wfa_config_no.HasValue)
                {
                    sqlParam += " AND wcl.wfa_config_no = @wfa_config_no ";
                }
                if (request.start_wfa_status.HasValue && request.end_wfa_status.HasValue)
                {
                    sqlParam += " AND wcl.wfa_status BETWEEN @start_wfa_status AND @end_wfa_status ";
                }
                else
                {
                    sqlParam += " AND wcl.wfa_status NOT BETWEEN 500 AND 599 ";
                }

                using (var conn = new NpgsqlConnection(PostgreDbNetmatrix))
                {
                    await conn.OpenAsync().ConfigureAwait(false);

                    var sql = $@" SELECT wfa_config_log_no, wfa_config_no, tdate, name, field_id, device_uuid, asset_id, asset_ad_id, asset_person_no, description, wfa_status, status, crt_id, crt_date, upd_id, upd_date
                                    FROM wfa_config_log wcl
                                   WHERE 1 = 1 
                                     AND wcl.status < 95
                                         {sqlParam}
                                   ORDER BY wcl.tdate DESC ";

                    var resultsData = (await conn.QueryAsync<dynamic>(sql, request).ConfigureAwait(false)).ToList();

                    apiResult.RetNumber = resultsData.Count;
                    apiResult.ResultSet = resultsData;
                }

                apiResult.RetCode = 200;
                apiResult.RetMsg = "Success";
            }
            catch (Exception ex)
            {
                apiResult.RetMsg = ex.ToString();
                apiResult.RetCode = -1;
                logger.Error(ex);
            }
            finally
            {
                AfterProcess();
            }

            return apiResult;
        }

        [HttpPost("GetRack")]
        public async Task<object> GetRack(ReqRack request)
        {
            try
            {
                using (var client = new BoltGraphClient(neu4jUri, neu4jUser, neu4jPwd))
                {
                    await client.ConnectAsync().ConfigureAwait(false);

                    var query = client.Cypher.WithParams(request);
                    query = query.Match("(sc:syscode)-[hh:HAS_ASSETTYPE]-(aa:asset)");
                    query = query.Where("1 = 1");
                    query = query.AndWhere("sc.codeGroup = 'assetType'");
                    query = query.AndWhere("sc.codeNo = 40");

                    if (!string.IsNullOrEmpty(request.assetId))
                    {
                        query = query.AndWhere((ReqRack aa) => aa.assetId == request.assetId);
                    }

                    if (request.status.HasValue)
                    {
                        query = query.AndWhere((ReqRack aa) => aa.status == request.status);
                    }
                    else
                    {
                        query = query.AndWhere("aa.status < 95");
                    }

                    IEnumerable<dynamic> dataList;
                    int dataTotalCount = 0;

                    if (!request.pageNumber.HasValue || !request.pageSize.HasValue)
                    {
                        dataList = await query.Return((aa) => new
                        {
                            assetId = aa.As<ResRack>().assetId,
                            name = aa.As<ResRack>().name,
                        }).ResultsAsync.ConfigureAwait(false);

                        dataTotalCount = dataList.Count();
                    }
                    else
                    {
                        dataList = await query.Return((aa) => new
                        {
                            assetId = aa.As<ResRack>().assetId,
                            name = aa.As<ResRack>().name,
                        })
                        .Skip((request.pageNumber - 1) * request.pageSize)
                        .Limit(request.pageSize)
                        .ResultsAsync.ConfigureAwait(false);

                        // 資料總數量
                        dataTotalCount = (await query
                                         .Return(() => Neo4jClient.Cypher.Return.As<int>("COUNT(DISTINCT aa)"))
                                         .ResultsAsync.ConfigureAwait(false)).FirstOrDefault();
                    }

                    dataList = dataList.Select(aa => new
                    {
                        assetId = aa.assetId,
                        name = aa.name
                    }).ToList();

                    apiResult.RetNumber = dataTotalCount;
                    apiResult.ResultSet = dataList;
                }

                apiResult.RetCode = 200;
            }
            catch (Exception ex)
            {
                apiResult.RetMsg = ex.ToString();
                apiResult.RetCode = -1;
                logger.Error(ex);
            }
            finally
            {
                AfterProcess();
            }

            return apiResult;
        }

        public class ReqRack
        {
            [Newtonsoft.Json.JsonProperty(PropertyName = "id")]
            public string? assetId { get; set; }
            public int? status { get; set; }
            public int? pageNumber { get; set; }
            public int? pageSize { get; set; }
        }
        public class ResRack : ReqRack
        {
            public string? name { get; set; }
        }

        public class ReqWfaConfig
        {
            public int? wfa_config_no { get; set; }
            public string? person_id { get; set; }
            public string? name { get; set; }
            public string? field_id { get; set; }
            public int? connect_type { get; set; }
            public string? account { get; set; }
            public string? password { get; set; }
            public string? processor_person_id { get; set; }
            public string? asset_id { get; set; }
            public string? asset_ad_id { get; set; }
            public int? asset_person_no { get; set; }
            public int? interconnect_no { get; set; }
            public string? device_uuid { get; set; }
            public int? status { get; set; }
            public DateTime? start_date { get; set; }
            public DateTime? end_date { get; set; }
            public string? description { get; set; }
            public string? crt_id { get; set; }
            public string? upd_id { get; set; }
        }
        public class ResWfaConfig : ReqWfaConfig
        {
            public DateTime? crt_date { get; set; }
            public DateTime? upd_date { get; set; }
        }
        public class ReqGetWfaConfig
        {
            public int? wfa_config_no { get; set; }
            public string? person_id { get; set; }
            public int? asset_person_no { get; set; }
            public int? network_element_no { get; set; }
            public int? asset_field_config_no { get; set; }
            public int? start_status { get; set; }
            public int? end_status { get; set; }
            public int? pageNumber { get; set; }
            public int? pageSize { get; set; }
        }
        public class ReqGetWfaConfigLog
        {
            public int? wfa_config_no { get; set; }
            public int? asset_person_no { get; set; }
            public int? start_wfa_status { get; set; }
            public int? end_wfa_status { get; set; }
        }

        public class ReqWfaConfigLog
        {
            public int? wfa_config_log_no { get; set; }
            public int? wfa_config_no { get; set; }
        }
        public class ResWfaConfigLog : ReqWfaConfigLog
        {
            public string? name { get; set; }
            public string? field_id { get; set; }
            // public int? connect_type { get; set; }
            public string? device_uuid { get; set; }
            public string? asset_id { get; set; }
            public int? status { get; set; }
            public string? description { get; set; }
            public string? crt_id { get; set; }
            public string? upd_id { get; set; }
            public DateTime? crt_date { get; set; }
            public DateTime? upd_date { get; set; }
        }

        public class ReqGetAsset
        {
            [Newtonsoft.Json.JsonProperty(PropertyName = "id")]
            public string? assetId { get; set; }
            public int? status { get; set; }
        }
        public class ReqUpdAsset : ReqGetAsset
        {
            // public string? SN { get; set; }
            // public string? type { get; set; }
            public string? name { get; set; }
        }
        public class ResUpdAsset : ReqUpdAsset
        {
        }
    }
}