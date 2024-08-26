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
using Neo4jClient.Extensions;
using Newtonsoft.Json;
using Npgsql;

namespace CollectorApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("CorsPolicy")]
    public class NetMatrixController : BaseApiController
    {
        private readonly IConfiguration _configuration;
        private string? neu4jUri;
        private string? neu4jUser;
        private string? neu4jPwd;
        private string? PostgreDbNetmatrix;

        public NetMatrixController(IConfiguration configuration)
        {
            _configuration = configuration;

            var neo4jDBSetting = _configuration.GetSection("neo4jDB");
            neu4jUri = neo4jDBSetting["Url"];
            neu4jUser = neo4jDBSetting["Account"];
            neu4jPwd = neo4jDBSetting["Password"];

            var postgreDBSetting = _configuration.GetSection("PostgreDB");
            PostgreDbNetmatrix = postgreDBSetting["Netmatrix"];
        }

        [HttpPost("GetAsset")]
        public async Task<ApiBaseModel> GetAsset(ReqAsset request)
        {
            try
            {
                // 過濾掉已綁定過的 asset
                List<string> assetIdFilterList = new List<string>();
                if (request.hiddenAssetIpAddress == true || request.hiddenAssetPerson == true || request.hiddenAssetField == true)
                {
                    using (var conn = new NpgsqlConnection(PostgreDbNetmatrix))
                    {
                        await conn.OpenAsync().ConfigureAwait(false);

                        if (request.hiddenAssetIpAddress == true)
                        {
                            var neSql = $@" SELECT DISTINCT asset_id  
                                                FROM network_element ne
                                                WHERE 1 = 1
                                                AND status < 95 ";

                            var neRes = (await conn.QueryAsync<string>(neSql).ConfigureAwait(false)).ToList();
                            assetIdFilterList.AddRange(neRes);
                        }

                        if (request.hiddenAssetPerson == true)
                        {
                            var apSql = $@" SELECT DISTINCT asset_id
                                                FROM asset_person ap
                                                WHERE 1 = 1
                                                AND status < 95 ";

                            var apRes = (await conn.QueryAsync<string>(apSql).ConfigureAwait(false)).ToList();
                            assetIdFilterList.AddRange(apRes);
                        }

                        if (request.hiddenAssetField == true)
                        {
                            var afcSql = $@" SELECT DISTINCT asset_id
                                                FROM asset_field_config afc
                                                WHERE 1 = 1
                                                AND status < 95 ";

                            var afcRes = (await conn.QueryAsync<string>(afcSql).ConfigureAwait(false)).ToList();
                            assetIdFilterList.AddRange(afcRes);
                        }
                    }
                    assetIdFilterList = assetIdFilterList.Distinct().ToList();
                }

                using (var client = new BoltGraphClient(neu4jUri, neu4jUser, neu4jPwd))
                {
                    await client.ConnectAsync().ConfigureAwait(false);

                    var query = client.Cypher.WithParams(request);
                    if (request.tagList != null && request.tagList.Count > 0)
                    {
                        query = query.Match("(aa:asset)-[ht:HAS_TAG]->(tt:tag)");
                    }
                    else
                    {
                        query = query.Match("(aa:asset)");
                        query = query.OptionalMatch("(aa)-[ht:HAS_TAG]->(tt:tag)");
                    }
                    query = query.Where("1 = 1");

                    if (request.assetIdList != null && request.assetIdList.Count() > 0)
                    {
                        query = query.AndWhere((ReqAsset aa) => aa.assetId.In(request.assetIdList));
                    }

                    if (assetIdFilterList != null && assetIdFilterList.Count > 0)
                    {
                        query = query.AndWhere((ReqAsset aa) => aa.assetId.NotIn(assetIdFilterList));
                    }

                    if (request.tagList != null && request.tagList.Count > 0)
                    {
                        query = query.AndWhere((ResTag tt) => tt.name.In(request.tagList));
                    }
                    if (request.excludedTagList != null && request.excludedTagList.Count > 0)
                    {
                        int index = 0;
                        foreach (var excludedTag in request.excludedTagList)
                        {
                            string paramName = $"excludedTag{index++}";
                            query = query.AndWhere($"NOT EXISTS {{(aa)-[:HAS_TAG]->(:tag {{name: ${paramName}}})}}");
                            query = query.WithParam(paramName, excludedTag);
                        }
                    }

                    if (!string.IsNullOrEmpty(request.assetId))
                    {
                        query = query.AndWhere((ReqAsset aa) => aa.assetId == request.assetId);
                    }


                    IEnumerable<dynamic> dataList;
                    int dataTotalCount = 0;

                    if (!request.pageNumber.HasValue || !request.pageSize.HasValue)
                    {
                        dataList = await query.Return((aa, tt) => new
                        {
                            assetId = aa.As<ResAsset>().assetId,
                            name = aa.As<ResAsset>().name,
                            attributes = aa.As<ResAsset>().attributes,
                            tagList = tt.CollectAs<ResTag>(),
                        }).ResultsAsync.ConfigureAwait(false);

                        dataTotalCount = dataList.Count();
                    }
                    else
                    {
                        dataList = await query.Return((aa, tt) => new
                        {
                            assetId = aa.As<ResAsset>().assetId,
                            name = aa.As<ResAsset>().name,
                            attributes = aa.As<ResAsset>().attributes,
                            tagList = tt.CollectAs<ResTag>(),
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
                        name = aa.name,
                        attributes = tool.DeserializeJson(aa.attributes),
                        tagList = aa.tagList,
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

        [HttpPost("GetField")]
        public async Task<ApiBaseModel> GetField(ReqField request)
        {
            try
            {
                using (var client = new BoltGraphClient(neu4jUri, neu4jUser, neu4jPwd))
                {
                    await client.ConnectAsync().ConfigureAwait(false);

                    var query = client.Cypher.WithParams(request);
                    query = query.Match("(aa:field)");
                    query = query.Where("1 = 1");

                    if (!string.IsNullOrEmpty(request.fieldId))
                    {
                        query = query.AndWhere((ReqField aa) => aa.fieldId == request.fieldId);
                    }

                    if (request.fieldIdList != null && request.fieldIdList.Count() > 0)
                    {
                        request.fieldIdList = request.fieldIdList.Distinct().ToList();
                        query = query.AndWhere((ReqField aa) => aa.fieldId.In(request.fieldIdList));
                    }

                    var fieldList = await query.Return((aa) => new
                    {
                        fieldId = aa.As<ResField>().fieldId,
                        name = aa.As<ResField>().name,
                    }).ResultsAsync.ConfigureAwait(false);

                    apiResult.RetNumber = fieldList.Count();
                    apiResult.ResultSet = fieldList;
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

        [HttpPost("GetEmployee")]
        public async Task<object> GetEmployee(ReqEmployee request)
        {
            try
            {
                using (var client = new BoltGraphClient(neu4jUri, neu4jUser, neu4jPwd))
                {
                    await client.ConnectAsync().ConfigureAwait(false);

                    var query = client.Cypher.WithParams(request);
                    query = query.Match("(employee:employee)-[:PROVIDED_BY]->(user:user)");
                    query = query.Where("1 = 1");

                    if (!string.IsNullOrEmpty(request.employeeId))
                    {
                        query = query.AndWhere((ReqEmployee employee) => employee.employeeId == request.employeeId);
                    }

                    IEnumerable<dynamic> dataList;
                    int dataTotalCount = 0;

                    if (!request.pageNumber.HasValue || !request.pageSize.HasValue)
                    {
                        dataList = await query.Return((employee, user) => new
                        {
                            employeeId = employee.As<ResEmployee>().employeeId,
                            userName = user.As<ResEmployee>().userName
                        }).ResultsAsync.ConfigureAwait(false);

                        dataTotalCount = dataList.Count();
                    }
                    else
                    {
                        dataList = await query.Return((employee, user) => new
                        {
                            employeeId = employee.As<ResEmployee>().employeeId,
                            userName = user.As<ResEmployee>().userName
                        })
                        .Skip((request.pageNumber - 1) * request.pageSize)
                        .Limit(request.pageSize)
                        .ResultsAsync.ConfigureAwait(false);

                        // 資料總數量
                        dataTotalCount = (await query
                                         .Return(() => Neo4jClient.Cypher.Return.As<int>("COUNT(DISTINCT aa)"))
                                         .ResultsAsync.ConfigureAwait(false)).FirstOrDefault();
                    }

                    apiResult.RetNumber = dataTotalCount = dataList.Count(); ;
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

        [HttpPost("GetPerson")]
        public async Task<object> GetPerson(ReqPerson request)
        {
            try
            {
                using (var client = new BoltGraphClient(neu4jUri, neu4jUser, neu4jPwd))
                {
                    await client.ConnectAsync().ConfigureAwait(false);

                    var query = client.Cypher.WithParams(request);
                    query = query.Match("(pp:person)");
                    query = query.Where("1 = 1");

                    if (!string.IsNullOrEmpty(request.personId))
                    {
                        query = query.AndWhere((ReqPerson pp) => pp.personId == request.personId);
                    }

                    IEnumerable<dynamic> dataList;
                    int dataTotalCount = 0;

                    if (!request.pageNumber.HasValue || !request.pageSize.HasValue)
                    {
                        dataList = await query.Return((pp) => new
                        {
                            personId = pp.As<ResPerson>().personId,
                            name = pp.As<ResPerson>().name
                        }).ResultsAsync.ConfigureAwait(false);

                        dataTotalCount = dataList.Count();
                    }
                    else
                    {
                        dataList = await query.Return((pp) => new
                        {
                            personId = pp.As<ResPerson>().personId,
                            name = pp.As<ResPerson>().name
                        })
                        .Skip((request.pageNumber - 1) * request.pageSize)
                        .Limit(request.pageSize)
                        .ResultsAsync.ConfigureAwait(false);

                        // 資料總數量
                        dataTotalCount = (await query
                                         .Return(() => Neo4jClient.Cypher.Return.As<int>("COUNT(DISTINCT pp)"))
                                         .ResultsAsync.ConfigureAwait(false)).FirstOrDefault();
                    }

                    apiResult.RetNumber = dataTotalCount = dataList.Count(); ;
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

        [HttpPost("AddOrUpdApiData")]
        public async Task<object> AddOrUpdApiData(List<UpdApiConfig> requestList)
        {
            List<string> errorParamList = new List<string>();

            try
            {
                using (var conn = new NpgsqlConnection(PostgreDbNetmatrix))
                {
                    await conn.OpenAsync().ConfigureAwait(false);
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            foreach (var req in requestList)
                            {
                                if (!req.method.HasValue)
                                {
                                    errorParamList.Add("method");
                                }
                                if (string.IsNullOrEmpty(req.endpoint))
                                {
                                    errorParamList.Add("endpoint");
                                }

                                var sql = req.api_config_no.HasValue ?
                                        $@"UPDATE api_config SET 
                                            name = @name, 
                                            method = @method, 
                                            endpoint = @endpoint, 
                                            request_header = @request_header, 
                                            request_body = @request_body, 
                                            status = @status, 
                                            upd_id = @upd_id, 
                                            upd_date = now() 
                                        WHERE api_config_no = @api_config_no"
                                                :
                                        $@"INSERT INTO api_config 
                                        (name, method, endpoint, request_header, request_body, status, crt_id, crt_date, upd_id, upd_date) 
                                        VALUES 
                                        (@name, @method, @endpoint, @request_header, @request_body, @status, @crt_id, now(), @upd_id, now())";

                                var num = await conn.ExecuteAsync(sql, req).ConfigureAwait(false);
                            }

                            if (errorParamList.Count > 0)
                            {
                                throw new Exception("Request param error.");
                            }

                            await transaction.CommitAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
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

        [HttpPost("GetApiData")]
        public async Task<object> GetApiData(ReqApiConfig request)
        {
            try
            {
                Tool.ApiTool apiTool = new Tool.ApiTool();
                var httpMethodTask = apiTool.FuncGetSysCode(new Tool.ApiTool.ReqSysCode() { codeGroup = "httpMethod" });
                var baseStatusTask = apiTool.FuncGetSysCode(new Tool.ApiTool.ReqSysCode() { codeGroup = "baseStatus" });

                string sqlParam = string.Empty;
                if (request.api_config_no.HasValue)
                {
                    sqlParam += " AND ac.api_config_no = @api_config_no ";
                }
                if (request.status.HasValue)
                {
                    sqlParam += " AND ac.status = @status ";
                }
                if (!string.IsNullOrEmpty(request.name))
                {
                    sqlParam += " AND ac.name LIKE '%' + @name + '%' ";
                }
                if (!string.IsNullOrEmpty(request.endpoint))
                {
                    sqlParam += " AND ac.endpoint LIKE '%' + @endpoint + '%' ";
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
                                    FROM api_config ac 
                                   WHERE 1 = 1
                                     and ac.status < 95
                                         {sqlParam} ";

                    int totalRecords = await conn.ExecuteScalarAsync<int>(countSql, request).ConfigureAwait(false);

                    var sql = $@" SELECT api_config_no, name, method, endpoint, request_header, request_body, status, crt_id, crt_date, upd_id, upd_date
                                    FROM api_config ac 
                                   WHERE 1 = 1
                                     and ac.status < 95
                                         {sqlParam}
                                   order by ac.upd_date desc
                                         {pageSql} ";

                    var resultsData = (await conn.QueryAsync<dynamic>(sql, request).ConfigureAwait(false)).ToList();

                    var httpMethodList = await httpMethodTask.ConfigureAwait(false);
                    var baseStatusList = await baseStatusTask.ConfigureAwait(false);

                    foreach (var row in resultsData)
                    {
                        {
                            var dataIndex = httpMethodList.FindIndex(x => x.codeNo == row.method);
                            row.methodName = dataIndex != -1 ? httpMethodList[dataIndex].codeName : null;
                        }
                        {
                            var dataIndex = baseStatusList.FindIndex(x => x.codeNo == row.status);
                            row.statusName = dataIndex != -1 ? baseStatusList[dataIndex].codeName : null;
                        }
                    }

                    apiResult.RetNumber = totalRecords;
                    apiResult.ResultSet = resultsData;
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

        [HttpPost("AddOrUpdIpAddress")]
        public async Task<object> AddOrUpdIpAddress(List<ReqIpAddress> requestList)
        {
            List<string> errorParamList = new List<string>();

            try
            {
                using (var conn = new NpgsqlConnection(PostgreDbNetmatrix))
                {
                    await conn.OpenAsync().ConfigureAwait(false);
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            foreach (var req in requestList)
                            {
                                if (string.IsNullOrEmpty(req.ip_address))
                                {
                                    errorParamList.Add("ip_address");
                                }
                                if (string.IsNullOrEmpty(req.mask))
                                {
                                    errorParamList.Add("mask");
                                }

                                var sql = req.ip_address_no.HasValue ?
                                        $@"UPDATE ip_address SET 
                                              name = @name, 
                                              ip_address = @ip_address, 
                                              mask = @mask, 
                                              dns = @dns, 
                                              dhcp_start = @dhcp_start, 
                                              dhcp_end = @dhcp_end, 
                                              gateway = @gateway, 
                                              type = @type, 
                                              field_id = @field_id, 
                                              status = @status, 
                                              upd_id = @upd_id, 
                                              upd_date = now() 
                                        WHERE ip_address_no = @ip_address_no"
                                                :
                                        $@"INSERT INTO ip_address 
                                              (name, ip_address, mask, dns, dhcp_start, dhcp_end, gateway, type, field_id, status, crt_id, crt_date, upd_id, upd_date) 
                                              VALUES 
                                              (@name, @ip_address, @mask, @dns, @dhcp_start, @dhcp_end, @gateway, @type, @field_id, @status, @crt_id, now(), @upd_id, now())";

                                var num = await conn.ExecuteAsync(sql, req).ConfigureAwait(false);
                            }

                            if (errorParamList.Count > 0)
                            {
                                throw new Exception("Request param error.");
                            }

                            await transaction.CommitAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
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

        [HttpPost("GetIpAddress")]
        public async Task<object> GetIpAddress(ReqGetIpAddress request)
        {
            try
            {
                string sqlParam = string.Empty;
                if (request.ip_address_no.HasValue)
                {
                    sqlParam += " and ia.ip_address_no = @ip_address_no ";
                }
                if (request.status.HasValue)
                {
                    sqlParam += " and ia.status = @status ";
                }
                if (request.type.HasValue)
                {
                    sqlParam += " and ia.type = @type ";
                }

                string pageSql = string.Empty;
                if (request.pageNumber.HasValue && request.pageSize.HasValue)
                {
                    pageSql = " LIMIT @pageSize OFFSET (@pageNumber - 1) * @pageSize ";
                }

                using (var conn = new NpgsqlConnection(PostgreDbNetmatrix))
                {
                    await conn.OpenAsync().ConfigureAwait(false);

                    var countSql = $@" select COUNT(1)
                                    from ip_address ia
                                   where 1 = 1
                                     and ia.status < 95
                                         {sqlParam} ";

                    int totalRecords = await conn.ExecuteScalarAsync<int>(countSql, request).ConfigureAwait(false);

                    var sql = $@" select ip_address_no, name, ip_address, mask, dns, dhcp_start, dhcp_end, gateway, type, field_id, status, crt_id, crt_date, upd_id, upd_date
                                    from ip_address ia
                                   where 1 = 1
                                     and ia.status < 95
                                         {sqlParam}
                                   order by ia.upd_date desc
                                         {pageSql} ";

                    Tool.ApiTool apiTool = new Tool.ApiTool();
                    var baseStatusTask = apiTool.FuncGetSysCode(new Tool.ApiTool.ReqSysCode() { codeGroup = "baseStatus" });
                    var useIpAddressTask = apiTool.FuncGetSysCode(new Tool.ApiTool.ReqSysCode() { codeGroup = "useIpAddress" });

                    var resultsData = (await conn.QueryAsync<dynamic>(sql, request).ConfigureAwait(false)).ToList();

                    // 從 neo4j 取得關聯資料
                    ConcurrentDictionary<string, Task<ApiBaseModel>> taskList = new ConcurrentDictionary<string, Task<ApiBaseModel>>();
                    List<string> fieldIdList = new List<string>();

                    foreach (var row in resultsData)
                    {
                        if (row.field_id != null)
                        {
                            fieldIdList.Add(row.field_id);
                        }
                    }

                    if (fieldIdList.Count > 0)
                    {
                        NetMatrixController netMatrixController = new NetMatrixController(_configuration);
                        taskList["fieldList"] = netMatrixController.GetField(new() { fieldIdList = fieldIdList });
                    }

                    Task t = Task.WhenAll(taskList.Values);
                    await t.ConfigureAwait(false);

                    var getFieldRes = taskList.ContainsKey("fieldList") ? await taskList["fieldList"].ConfigureAwait(false) : null;

                    var baseStatusList = await baseStatusTask.ConfigureAwait(false);
                    var useIpAddressList = await useIpAddressTask.ConfigureAwait(false);

                    List<dynamic> fieldList = new List<dynamic>();

                    if (getFieldRes != null && getFieldRes.RetCode == 200 && getFieldRes.ResultSet != null)
                    {
                        var dataList = JsonConvert.DeserializeObject<List<dynamic>>(JsonConvert.SerializeObject(getFieldRes.ResultSet));
                        fieldList.AddRange(dataList);
                    }

                    foreach (var row in resultsData)
                    {
                        {
                            var dataIndex = baseStatusList.FindIndex(x => x.codeNo == row.status);
                            row.statusName = dataIndex != -1 ? baseStatusList[dataIndex].codeName : null;
                        }
                        {
                            var dataIndex = useIpAddressList.FindIndex(x => x.codeNo == row.type);
                            row.type_name = dataIndex != -1 ? useIpAddressList[dataIndex].codeName : null;
                        }
                        {
                            var dataIndex = fieldList.FindIndex(x => x.fieldId == row.field_id);
                            row.field_name = dataIndex != -1 ? (string?)(fieldList[dataIndex].name) : null;
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

        [HttpPost("AddOrUpdAssetIp")]
        public async Task<object> AddOrUpdAssetIp(List<ReqAssetIp> requestList)
        {
            List<string> errorParamList = new List<string>();

            try
            {
                using (var conn = new NpgsqlConnection(PostgreDbNetmatrix))
                {
                    await conn.OpenAsync().ConfigureAwait(false);
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            foreach (var request in requestList)
                            {
                                if (string.IsNullOrEmpty(request.name))
                                {
                                    errorParamList.Add("name");
                                }
                                if (!request.status.HasValue)
                                {
                                    errorParamList.Add("status");
                                }

                                var sql = request.network_element_no.HasValue ?
                                        $@"UPDATE network_element SET 
                                              ip_address_no = @ip_address_no, 
                                              asset_id = @asset_id, 
                                              name = @name, 
                                              ip_mode = @ip_mode, 
                                              interface_index = @interface_index, 
                                              status = @status, 
                                              upd_id = @upd_id, 
                                              upd_date = now() 
                                        WHERE network_element_no = @network_element_no"
                                                :
                                        $@"INSERT INTO network_element 
                                              (ip_address_no, asset_id, name, ip_mode, interface_index, status, crt_id, crt_date, upd_id, upd_date) 
                                              VALUES 
                                              (@ip_address_no, @asset_id, @name, @ip_mode, @interface_index, @status, @crt_id, now(), @upd_id, now())";

                                var num = await conn.ExecuteAsync(sql, request).ConfigureAwait(false);
                            }
                            if (errorParamList.Count > 0)
                            {
                                throw new Exception("Request param error.");
                            }

                            await transaction.CommitAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
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

        [HttpPost("GetAssetIp")]
        public async Task<object> GetAssetIp(ReqGetAssetIp request)
        {
            try
            {
                string sqlParam = string.Empty;
                if (!string.IsNullOrEmpty(request.asset_Id))
                {
                    sqlParam += " and ne.asset_id = @asset_id ";
                }
                if (request.status.HasValue)
                {
                    sqlParam += " and ne.status = @status ";
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
                                    FROM network_element ne
                                        LEFT JOIN ip_address ia 
                                                ON ia.ip_address_no = ne.ip_address_no 
                                                AND ia.status = 80
                                    WHERE 1 = 1
                                      AND ne.status < 95
                                         {sqlParam} ";

                    int totalRecords = await conn.ExecuteScalarAsync<int>(countSql, request).ConfigureAwait(false);

                    var sql = $@" SELECT ne.network_element_no, ne.ip_address_no, ne.asset_id, ne.name, ne.ip_mode, ne.interface_index, ne.status, ne.crt_id, ne.crt_date, ne.upd_id, ne.upd_date
                                        , ia.name AS ip_address_name, ia.ip_address, ia.mask
                                    FROM network_element ne
                                        LEFT JOIN ip_address ia 
                                                ON ia.ip_address_no = ne.ip_address_no 
                                                AND ia.status = 80
                                    WHERE 1 = 1
                                      AND ne.status < 95
                                         {sqlParam}
                                    ORDER BY ne.upd_date DESC
                                         {pageSql} ";

                    Tool.ApiTool apiTool = new Tool.ApiTool();
                    var baseStatusTask = apiTool.FuncGetSysCode(new Tool.ApiTool.ReqSysCode() { codeGroup = "baseStatus" });
                    var ipTypeTask = apiTool.FuncGetSysCode(new Tool.ApiTool.ReqSysCode() { codeGroup = "ipType" });

                    var resultsData = (await conn.QueryAsync<dynamic>(sql, request).ConfigureAwait(false)).ToList();

                    // 從 neo4j 取得關聯資料
                    ConcurrentDictionary<string, Task<ApiBaseModel>> taskList = new ConcurrentDictionary<string, Task<ApiBaseModel>>();
                    List<string> assetIdList = new List<string>();

                    foreach (var row in resultsData)
                    {
                        if (row.asset_id != null)
                        {
                            assetIdList.Add(row.asset_id);
                        }
                    }

                    if (assetIdList.Count > 0)
                    {
                        NetMatrixController netMatrixController = new NetMatrixController(_configuration);
                        taskList["assetList"] = netMatrixController.GetAsset(new() { assetIdList = assetIdList });
                    }

                    Task t = Task.WhenAll(taskList.Values);
                    await t.ConfigureAwait(false);

                    var getAssetRes = taskList.ContainsKey("assetList") ? await taskList["assetList"].ConfigureAwait(false) : null;
                    var baseStatusList = await baseStatusTask.ConfigureAwait(false);
                    var ipTypeList = await ipTypeTask.ConfigureAwait(false);

                    List<dynamic> assetList = new List<dynamic>();

                    if (getAssetRes != null && getAssetRes.RetCode == 200 && getAssetRes.ResultSet != null)
                    {
                        var dataList = JsonConvert.DeserializeObject<List<dynamic>>(JsonConvert.SerializeObject(getAssetRes.ResultSet));
                        assetList.AddRange(dataList);
                    }

                    foreach (var row in resultsData)
                    {
                        {
                            var dataIndex = assetList.FindIndex(x => x.assetId == row.asset_id);
                            row.asset_name = dataIndex != -1 ? (string?)(assetList[dataIndex].name) : null;
                        }
                        {
                            var dataIndex = baseStatusList.FindIndex(x => x.codeNo == row.status);
                            row.statusName = dataIndex != -1 ? baseStatusList[dataIndex].codeName : null;
                        }
                        {
                            var dataIndex = ipTypeList.FindIndex(x => x.codeNo == row.ip_mode);
                            row.ip_mode_Name = dataIndex != -1 ? ipTypeList[dataIndex].codeName : null;
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

        [HttpPost("AddOrUpdAssetFieldConfig")]
        public async Task<object> AddOrUpdAssetFieldConfig(List<ReqAssetFieldConfig> requestList)
        {
            List<string> errorParamList = new List<string>();

            try
            {
                using (var conn = new NpgsqlConnection(PostgreDbNetmatrix))
                {
                    await conn.OpenAsync().ConfigureAwait(false);
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            foreach (var request in requestList)
                            {
                                if (string.IsNullOrEmpty(request.name))
                                {
                                    errorParamList.Add("name");
                                }
                                if (!request.status.HasValue)
                                {
                                    errorParamList.Add("status");
                                }

                                var sql = request.asset_field_config_no.HasValue ?
                                        $@"UPDATE asset_field_config SET 
                                            asset_id = @asset_id, 
                                            rack_id = @rack_id, 
                                            field_id = @field_id, 
                                            start_unit = @start_unit, 
                                            name = @name, 
                                            status = @status, 
                                            upd_id = @upd_id, 
                                            upd_date = now() 
                                        WHERE asset_field_config_no = @asset_field_config_no"
                                                :
                                        $@"INSERT INTO asset_field_config 
                                        (asset_id, rack_id, field_id, start_unit, name, status, crt_id, crt_date, upd_id, upd_date) 
                                        VALUES 
                                        (@asset_id, @rack_id, @field_id, @start_unit, @name, @status, @crt_id, now(), @upd_id, now())";

                                var num = await conn.ExecuteAsync(sql, request).ConfigureAwait(false);
                            }
                            if (errorParamList.Count > 0)
                            {
                                throw new Exception("Request param error.");
                            }

                            await transaction.CommitAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
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

        [HttpPost("GetAssetFieldConfig")]
        public async Task<object> GetAssetFieldConfig(ReqGetAssetFieldConfig request)
        {
            try
            {
                string sqlParam = string.Empty;
                if (request.asset_field_config_no.HasValue)
                {
                    sqlParam += " AND afc.asset_field_config_no = @asset_field_config_no ";
                }
                if (request.status.HasValue)
                {
                    sqlParam += " AND afc.status = @status ";
                }
                if (!string.IsNullOrEmpty(request.name))
                {
                    sqlParam += " AND afc.name LIKE '%' + @name + '%' ";
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
                                    FROM asset_field_config afc
                                   WHERE 1 = 1
                                     and afc.status < 95
                                         {sqlParam} ";

                    int totalRecords = await conn.ExecuteScalarAsync<int>(countSql, request).ConfigureAwait(false);

                    var sql = $@" SELECT asset_field_config_no, asset_id, rack_id, field_id, start_unit, name, status, crt_id, crt_date, upd_id, upd_date
                                    FROM asset_field_config afc
                                   WHERE 1 = 1
                                     AND afc.status < 95
                                         {sqlParam}
                                   ORDER BY afc.upd_date DESC
                                         {pageSql} ";

                    var resultsData = (await conn.QueryAsync<dynamic>(sql, request).ConfigureAwait(false)).ToList();

                    Tool.ApiTool apiTool = new Tool.ApiTool();
                    var baseStatusTask = apiTool.FuncGetSysCode(new Tool.ApiTool.ReqSysCode() { codeGroup = "baseStatus" });

                    // 從 neo4j 取得關聯資料
                    Dictionary<string, Task<ApiBaseModel>> taskList = new Dictionary<string, Task<ApiBaseModel>>();
                    List<string> assetIdList = new List<string>();
                    List<string> fieldIdList = new List<string>();

                    foreach (var row in resultsData)
                    {
                        if (row.asset_id != null)
                        {
                            assetIdList.Add(row.asset_id);
                        }
                        if (row.rack_id != null)
                        {
                            assetIdList.Add(row.rack_id);
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
                    if (fieldIdList.Count > 0)
                    {
                        NetMatrixController netMatrixController = new NetMatrixController(_configuration);
                        taskList["fieldList"] = netMatrixController.GetField(new() { fieldIdList = fieldIdList });
                    }

                    Task t = Task.WhenAll(taskList.Values);
                    await t.ConfigureAwait(false);

                    var getAssetRes = taskList.ContainsKey("assetList") ? await taskList["assetList"].ConfigureAwait(false) : null;
                    var getFieldRes = taskList.ContainsKey("fieldList") ? await taskList["fieldList"].ConfigureAwait(false) : null;
                    var baseStatusList = await baseStatusTask.ConfigureAwait(false);

                    List<dynamic> assetList = new List<dynamic>();
                    List<dynamic> fieldList = new List<dynamic>();

                    if (getAssetRes != null && getAssetRes.RetCode == 200 && getAssetRes.ResultSet != null)
                    {
                        var dataList = JsonConvert.DeserializeObject<List<dynamic>>(JsonConvert.SerializeObject(getAssetRes.ResultSet));
                        assetList.AddRange(dataList);
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
                        }
                        {
                            var dataIndex = assetList.FindIndex(x => x.assetId == row.rack_id);
                            row.rack_name = dataIndex != -1 ? (string?)(assetList[dataIndex].name) : null;
                        }
                        {
                            var dataIndex = fieldList.FindIndex(x => x.fieldId == row.field_id);
                            row.field_name = dataIndex != -1 ? (string?)(fieldList[dataIndex].name) : null;
                        }
                        {
                            var dataIndex = baseStatusList.FindIndex(x => x.codeNo == row.status);
                            row.statusName = dataIndex != -1 ? baseStatusList[dataIndex].codeName : null;
                        }
                    }

                    apiResult.RetNumber = totalRecords;
                    apiResult.ResultSet = resultsData;
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

        [HttpPost("AddOrUpdAssetPerson")]
        public async Task<object> AddOrUpdAssetPerson(List<ReqAssetPerson> requestList)
        {
            List<string> errorParamList = new List<string>();

            try
            {
                using (var conn = new NpgsqlConnection(PostgreDbNetmatrix))
                {
                    await conn.OpenAsync().ConfigureAwait(false);
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            foreach (var request in requestList)
                            {
                                if (string.IsNullOrEmpty(request.name))
                                {
                                    errorParamList.Add("name");
                                }
                                if (!request.status.HasValue)
                                {
                                    errorParamList.Add("status");
                                }

                                var sql = request.asset_person_no.HasValue ?
                                        $@"UPDATE asset_person
                                        SET asset_id = @asset_id
                                            ,person_id = @person_id
                                            ,name = @name
                                            ,status = @status
                                            ,upd_id = @upd_id
                                            ,upd_date = now()
                                        WHERE asset_person_no = @asset_person_no"
                                                :
                                        $@"INSERT INTO asset_person  
                                            (asset_id, person_id, name, status, crt_id, crt_date, upd_id, upd_date) 
                                            VALUES 
                                            (@asset_id, @person_id, @name, @status, @crt_id, now(), @upd_id, now())";

                                var num = await conn.ExecuteAsync(sql, request).ConfigureAwait(false);
                            }
                            if (errorParamList.Count > 0)
                            {
                                throw new Exception("Request param error.");
                            }

                            await transaction.CommitAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
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

        [HttpPost("GetAssetPerson")]
        public async Task<object> GetAssetPerson(ReqGetAssetPerson request)
        {
            try
            {
                string sqlParam = string.Empty;
                if (request.asset_person_no.HasValue)
                {
                    sqlParam += " AND ap.asset_person_no = @asset_person_no ";
                }
                if (request.status.HasValue)
                {
                    sqlParam += " AND ap.status = @status ";
                }
                if (!string.IsNullOrEmpty(request.name))
                {
                    sqlParam += " AND ap.name LIKE '%' + @name + '%' ";
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
                                         FROM asset_person ap
                                        WHERE 1 = 1
                                          AND ap.status < 95
                                              {sqlParam} ";

                    int totalRecords = await conn.ExecuteScalarAsync<int>(countSql, request).ConfigureAwait(false);

                    var sql = $@" SELECT asset_person_no, asset_id, person_id, name, status, crt_id, crt_date, upd_id, upd_date
                                    FROM asset_person ap
                                   WHERE 1 = 1
                                     AND ap.status < 95
                                         {sqlParam}
                                   ORDER BY ap.upd_date DESC
                                         {pageSql} ";

                    var resultsData = (await conn.QueryAsync<dynamic>(sql, request).ConfigureAwait(false)).ToList();

                    Tool.ApiTool apiTool = new Tool.ApiTool();
                    var baseStatusTask = apiTool.FuncGetSysCode(new Tool.ApiTool.ReqSysCode() { codeGroup = "baseStatus" });

                    // 從 neo4j 取得關聯資料
                    Dictionary<string, Task<ApiBaseModel>> taskList = new Dictionary<string, Task<ApiBaseModel>>();
                    List<string> assetIdList = new List<string>();
                    List<string> personIdList = new List<string>();

                    foreach (var row in resultsData)
                    {
                        if (row.asset_id != null)
                        {
                            assetIdList.Add(row.asset_id);
                        }
                        if (row.person_id != null)
                        {
                            personIdList.Add(row.person_id);
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

                    Task t = Task.WhenAll(taskList.Values);
                    await t.ConfigureAwait(false);

                    var getAssetRes = taskList.ContainsKey("assetList") ? await taskList["assetList"].ConfigureAwait(false) : null;
                    var getPersonRes = taskList.ContainsKey("personList") ? await taskList["personList"].ConfigureAwait(false) : null;
                    var baseStatusList = await baseStatusTask.ConfigureAwait(false);

                    List<dynamic> assetList = new List<dynamic>();
                    List<dynamic> personList = new List<dynamic>();

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

                    foreach (var row in resultsData)
                    {
                        {
                            var dataIndex = assetList.FindIndex(x => x.assetId == row.asset_id);
                            row.asset_name = dataIndex != -1 ? (string?)(assetList[dataIndex].name) : null;
                        }
                        {
                            var dataIndex = assetList.FindIndex(x => x.assetId == row.rack_id);
                            row.rack_name = dataIndex != -1 ? (string?)(assetList[dataIndex].name) : null;
                        }
                        if (row.person_id != null)
                        {
                            var dataIndex = personList.FindIndex(x => x.personId == row.person_id);
                            row.person_name = dataIndex != -1 ? (string?)(personList[dataIndex].name) : null;
                        }
                        {
                            var dataIndex = baseStatusList.FindIndex(x => x.codeNo == row.status);
                            row.statusName = dataIndex != -1 ? baseStatusList[dataIndex].codeName : null;
                        }

                    }

                    apiResult.RetNumber = totalRecords;
                    apiResult.ResultSet = resultsData;
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

        [HttpPost("AddOrUpdInterconnect")]
        public async Task<object> AddOrUpdInterconnect(List<ReqInterconnect> requestList)
        {
            List<string> errorParamList = new List<string>();

            try
            {
                using (var conn = new NpgsqlConnection(PostgreDbNetmatrix))
                {
                    await conn.OpenAsync().ConfigureAwait(false);
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            foreach (var request in requestList)
                            {
                                if (string.IsNullOrEmpty(request.name))
                                {
                                    errorParamList.Add("name");
                                }
                                if (!request.status.HasValue)
                                {
                                    errorParamList.Add("status");
                                }

                                var sql = request.interconnect_no.HasValue ?
                                        $@"UPDATE interconnect
                                            SET name = @name
                                                ,source_asset_id = @source_asset_id
                                                ,source_interface_id = @source_interface_id
                                                ,connect_type = @connect_type
                                                ,destination_asset_id = @destination_asset_id
                                                ,destination_interface_id = @destination_interface_id
                                                ,status = @status
                                                ,upd_id = @upd_id
                                                ,upd_date = now()
                                            WHERE interconnect_no = @interconnect_no"
                                                :
                                        $@"INSERT INTO interconnect  
                                        (name, source_asset_id, source_interface_id, connect_type, destination_asset_id, destination_interface_id, status, crt_id, crt_date, upd_id, upd_date) 
                                        VALUES 
                                        (@name, @source_asset_id, @source_interface_id, @connect_type, @destination_asset_id, @destination_interface_id, @status, @crt_id, now(), @upd_id, now())";

                                var num = await conn.ExecuteAsync(sql, request).ConfigureAwait(false);
                            }
                            if (errorParamList.Count > 0)
                            {
                                throw new Exception("Request param error.");
                            }

                            await transaction.CommitAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
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

        [HttpPost("GetInterconnect")]
        public async Task<object> GetInterconnect(ReqGetInterconnect request)
        {
            try
            {
                string sqlParam = string.Empty;
                if (request.interconnect_no.HasValue)
                {
                    sqlParam += " AND icnt.interconnect_no = @interconnect_no ";
                }
                if (request.status.HasValue)
                {
                    sqlParam += " AND icnt.status = @status ";
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
                                         FROM interconnect icnt
                                        WHERE 1 = 1
                                          AND icnt.status < 95
                                              {sqlParam} ";

                    int totalRecords = await conn.ExecuteScalarAsync<int>(countSql, request).ConfigureAwait(false);

                    var sql = $@" SELECT interconnect_no, name, source_asset_id, source_interface_id, connect_type, destination_asset_id, destination_interface_id, status, crt_id, crt_date, upd_id, upd_date
                                    FROM interconnect icnt
                                   WHERE 1 = 1
                                     AND icnt.status < 95
                                         {sqlParam}
                                   ORDER BY icnt.upd_date DESC
                                         {pageSql} ";

                    var resultsData = (await conn.QueryAsync<dynamic>(sql, request).ConfigureAwait(false)).ToList();

                    Tool.ApiTool apiTool = new Tool.ApiTool();
                    var baseStatusTask = apiTool.FuncGetSysCode(new Tool.ApiTool.ReqSysCode() { codeGroup = "baseStatus" });
                    var assetConnectTypeTask = apiTool.FuncGetSysCode(new Tool.ApiTool.ReqSysCode() { codeGroup = "assetConnectType" });

                    // 從 neo4j 取得關聯資料
                    Dictionary<string, Task<ApiBaseModel>> taskList = new Dictionary<string, Task<ApiBaseModel>>();
                    List<string> assetIdList = new List<string>();

                    foreach (var row in resultsData)
                    {
                        if (row.source_asset_id != null)
                        {
                            assetIdList.Add(row.source_asset_id);
                        }
                        if (row.destination_asset_id != null)
                        {
                            assetIdList.Add(row.destination_asset_id);
                        }
                    }

                    if (assetIdList.Count > 0)
                    {
                        NetMatrixController netMatrixController = new NetMatrixController(_configuration);
                        taskList["assetList"] = netMatrixController.GetAsset(new() { assetIdList = assetIdList });
                    }

                    Task t = Task.WhenAll(taskList.Values);
                    await t.ConfigureAwait(false);

                    var getAssetRes = taskList.ContainsKey("assetList") ? await taskList["assetList"].ConfigureAwait(false) : null;
                    var baseStatusList = await baseStatusTask.ConfigureAwait(false);
                    var assetConnectTypeList = await assetConnectTypeTask.ConfigureAwait(false);

                    List<dynamic> assetList = new List<dynamic>();

                    if (getAssetRes != null && getAssetRes.RetCode == 200 && getAssetRes.ResultSet != null)
                    {
                        var dataList = JsonConvert.DeserializeObject<List<dynamic>>(JsonConvert.SerializeObject(getAssetRes.ResultSet));
                        assetList.AddRange(dataList);
                    }

                    foreach (var row in resultsData)
                    {
                        {
                            var dataIndex = assetList.FindIndex(x => x.assetId == row.source_asset_id);
                            row.source_asset_name = dataIndex != -1 ? (string?)(assetList[dataIndex].name) : null;
                        }
                        {
                            var dataIndex = assetList.FindIndex(x => x.assetId == row.destination_asset_id);
                            row.destination_asset_name = dataIndex != -1 ? (string?)(assetList[dataIndex].name) : null;
                        }
                        {
                            var dataIndex = baseStatusList.FindIndex(x => x.codeNo == row.status);
                            row.statusName = dataIndex != -1 ? baseStatusList[dataIndex].codeName : null;
                        }
                        {
                            var dataIndex = assetConnectTypeList.FindIndex(x => x.codeNo == row.connect_type);
                            row.connect_type_name = dataIndex != -1 ? assetConnectTypeList[dataIndex].codeName : null;
                        }

                        // 還沒想好如何串 interface，暫時 null
                        row.source_interface_name = null;
                        row.destination_interface_name = null;
                    }

                    apiResult.RetNumber = totalRecords;
                    apiResult.ResultSet = resultsData;
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

        public class ReqInterconnect
        {
            public int? interconnect_no { get; set; }
            public string? name { get; set; }
            public string? source_asset_id { get; set; }
            public string? source_interface_id { get; set; }
            public int? connect_type { get; set; }
            public string? destination_asset_id { get; set; }
            public string? destination_interface_id { get; set; }
            public int? status { get; set; }
            public string? crt_id { get; set; }
            public string? upd_id { get; set; }
        }
        public class ReqGetInterconnect
        {
            public int? interconnect_no { get; set; }
            public int? status { get; set; }
            public int? pageNumber { get; set; }
            public int? pageSize { get; set; }
        }
        public class ResInterconnect : ReqInterconnect
        {
            public string? source_asset_name { get; set; }
            public string? source_interface_name { get; set; }
            public int? connect_type_name { get; set; }
            public string? destination_asset_name { get; set; }
            public string? destination_interface_name { get; set; }
            public int? statusName { get; set; }
            public DateTime? crt_date { get; set; }
            public DateTime? upd_date { get; set; }
        }

        public class ReqAssetPerson
        {
            public int? asset_person_no { get; set; }
            public string? asset_id { get; set; }
            public string? person_id { get; set; }
            public string? name { get; set; }
            public int? status { get; set; }
            public string? crt_id { get; set; }
            public string? upd_id { get; set; }
        }
        public class ResAssetPerson : ReqAssetPerson
        {
            public string? asset_name { get; set; }
            public string? person_name { get; set; }
        }
        public class ReqGetAssetPerson
        {
            public int? asset_person_no { get; set; }
            public string? name { get; set; }
            public int? status { get; set; }
            public int? pageNumber { get; set; }
            public int? pageSize { get; set; }
        }
        public class ResGetAssetPerson
        {
            public int? asset_person_no { get; set; }
            public string? asset_id { get; set; }
            public string? asset_name { get; set; }
            public string? person_id { get; set; }
            public string? person_name { get; set; }
            public string? name { get; set; }
            public int? status { get; set; }
            public string? statusName { get; set; }
            public string? crt_id { get; set; }
            public string? upd_id { get; set; }
        }

        public class ReqAssetFieldConfig
        {
            public int? asset_field_config_no { get; set; }
            public string? asset_id { get; set; }
            public string? rack_id { get; set; }
            public string? field_id { get; set; }
            public double? start_unit { get; set; }
            public string? name { get; set; }
            public int? status { get; set; }
            public string? crt_id { get; set; }
            public string? upd_id { get; set; }
        }
        public class ReqGetAssetFieldConfig
        {
            public int? asset_field_config_no { get; set; }
            public string? name { get; set; }
            public int? status { get; set; }
            public int? pageNumber { get; set; }
            public int? pageSize { get; set; }
        }
        public class ResAssetFieldConfig
        {
            public int? asset_field_config_no { get; set; }
            public string? asset_id { get; set; }
            public string? rack_id { get; set; }
            public string? field_id { get; set; }
            public string? name { get; set; }
            public int? status { get; set; }
            public string? crt_id { get; set; }
            public string? upd_id { get; set; }
        }

        public class ReqAsset
        {
            [Newtonsoft.Json.JsonProperty(PropertyName = "id")]
            public string? assetId { get; set; }
            public List<string>? assetIdList { get; set; }
            public List<string>? tagList { get; set; }
            public List<string>? excludedTagList { get; set; }
            public bool? hiddenAssetPerson { get; set; }
            public bool? hiddenAssetField { get; set; }
            public bool? hiddenAssetIpAddress { get; set; }
            public int? pageNumber { get; set; }
            public int? pageSize { get; set; }
        }
        public class ResAsset : ReqAsset
        {
            public string? name { get; set; }
            public string? attributes { get; set; }
        }
        public class ResTag
        {
            [Newtonsoft.Json.JsonProperty(PropertyName = "id")]
            public string? tagId { get; set; }
            public string? name { get; set; }
        }
        public class ReqGetAssetIp
        {
            public string? asset_Id { get; set; }
            public int? status { get; set; }
            public int? pageNumber { get; set; }
            public int? pageSize { get; set; }
        }
        public class ReqAssetIp
        {
            public string? asset_Id { get; set; }
            public int? status { get; set; }
            public int? network_element_no { get; set; }
            public int? ip_address_no { get; set; }
            public string? name { get; set; }
            public int? ip_mode { get; set; }
            public int? interface_index { get; set; }
            public string? crt_id { get; set; }
            public string? upd_id { get; set; }
        }
        public class ResAssetIp : ReqAssetIp
        {
            public DateTime? crt_date { get; set; }
            public DateTime? upd_date { get; set; }
        }

        public class ReqField
        {
            [Newtonsoft.Json.JsonProperty(PropertyName = "id")]
            public string? fieldId { get; set; }
            public List<string>? fieldIdList { get; set; }
        }
        public class ResField
        {
            [Newtonsoft.Json.JsonProperty(PropertyName = "id")]
            public string? fieldId { get; set; }
            public string? name { get; set; }
        }

        public class ReqEmployee
        {
            [Newtonsoft.Json.JsonProperty(PropertyName = "id")]
            public string? employeeId { get; set; }
            public int? pageNumber { get; set; }
            public int? pageSize { get; set; }
        }
        public class ResEmployee
        {
            [Newtonsoft.Json.JsonProperty(PropertyName = "id")]
            public string? employeeId { get; set; }
            public string? userName { get; set; }
        }

        public class ReqPerson
        {
            [Newtonsoft.Json.JsonProperty(PropertyName = "id")]
            public string? personId { get; set; }
            public int? pageNumber { get; set; }
            public int? pageSize { get; set; }
        }
        public class ResPerson
        {
            [Newtonsoft.Json.JsonProperty(PropertyName = "id")]
            public string? personId { get; set; }
            public string? name { get; set; }
        }

        public class ReqApiConfig
        {
            public int? api_config_no { get; set; }
            public string? name { get; set; }
            public int? status { get; set; }
            public int? method { get; set; }
            public string? endpoint { get; set; }
            public int? pageNumber { get; set; }
            public int? pageSize { get; set; }
        }
        public class ResApiConfig : ReqApiConfig
        {
            public string? request_header { get; set; }
            public string? request_body { get; set; }
            public string? statusName { get; set; }
            public string? methodName { get; set; }
            public string? crt_id { get; set; }
            public string? upd_id { get; set; }
        }
        public class UpdApiConfig : ResApiConfig
        {
        }

        public class ReqGetIpAddress
        {
            public int? ip_address_no { get; set; }
            public int? status { get; set; }
            public int? type { get; set; }
            public int? pageNumber { get; set; }
            public int? pageSize { get; set; }
        }
        public class ReqIpAddress
        {
            public int? ip_address_no { get; set; }
            public int? status { get; set; }
            public int? type { get; set; }
            public string? name { get; set; }
            public string? ip_address { get; set; }
            public string? mask { get; set; }
            public string? dns { get; set; }
            public string? dhcp_start { get; set; }
            public string? dhcp_end { get; set; }
            public string? gateway { get; set; }
            public string? field_id { get; set; }
            public string? crt_id { get; set; }
            public string? upd_id { get; set; }
        }
    }
}


// curl 'http://10.88.21.196:5064/api/NetMatrix/GetRack' \
// -H 'Accept: application/json, text/plain, */*' \
// -H 'Accept-Language: zh-TW,zh;q=0.9,en-US;q=0.8,en;q=0.7' \
// -H 'Content-Type: application/json' \
// --data-raw '{
// "id": "	00919a73-1fb4-4ab8-8f4a-d6902c92d841"
// }
// ' \
// --compressed