using System.Text;
using MailKit.Security;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using Neo4jClient;
using Neo4jClient.Extensions;
using Newtonsoft.Json;

namespace CollectorApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("CorsPolicy")]
    public class CommonController : BaseApiController
    {
        private readonly IConfiguration _configuration;
        private string? neu4jUri;
        private string? neu4jUser;
        private string? neu4jPwd;
        public CommonController(IConfiguration configuration)
        {
            _configuration = configuration;

            var neo4jDBSetting = _configuration.GetSection("neo4jDB");
            neu4jUri = neo4jDBSetting["Url"];
            neu4jUser = neo4jDBSetting["Account"];
            neu4jPwd = neo4jDBSetting["Password"];
        }

        [HttpPost("GetSysCode")]
        public async Task<object> GetSysCode(ReqSysCode request)
        {
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

                    apiResult.ResultSet = sysCodeList.Count();
                    apiResult.ResultSet = sysCodeList;
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

        [HttpPost("GetUser")]
        public async Task<ApiBaseModel> GetUser(ReqGetUser request)
        {
            try
            {
                using (var client = new BoltGraphClient(neu4jUri, neu4jUser, neu4jPwd))
                {
                    await client.ConnectAsync().ConfigureAwait(false);

                    var query = client.Cypher.WithParams(request);
                    query = query.Match("(aa:user)");
                    query = query.Where("1 = 1");

                    if (request.userIdList != null && request.userIdList.Count() > 0)
                    {
                        request.userIdList = request.userIdList.Distinct().ToList();
                        query = query.AndWhere((ReqGetUser aa) => aa.userId.In(request.userIdList));
                    }

                    if (!string.IsNullOrEmpty(request.userId))
                    {
                        query = query.AndWhere((ReqGetUser aa) => aa.userId == request.userId);
                    }

                    query = query.AndWhere("(aa.status < 95 OR aa.status IS NULL)");

                    IEnumerable<dynamic> dataList;
                    int dataTotalCount = 0;

                    if (!request.pageNumber.HasValue || !request.pageSize.HasValue)
                    {
                        dataList = await query.Return((aa) => new
                        {
                            userId = aa.As<ResGetUser>().userId,
                            name = aa.As<ResGetUser>().name,
                        }).ResultsAsync.ConfigureAwait(false);

                        dataTotalCount = dataList.Count();
                    }
                    else
                    {
                        dataList = await query.Return((aa) => new
                        {
                            userId = aa.As<ResGetUser>().userId,
                            name = aa.As<ResGetUser>().name,
                        })
                        .Skip((request.pageNumber - 1) * request.pageSize)
                        .Limit(request.pageSize)
                        .ResultsAsync.ConfigureAwait(false);

                        // 資料總數量
                        dataTotalCount = (await query
                                         .Return(() => Neo4jClient.Cypher.Return.As<int>("COUNT(DISTINCT aa)"))
                                         .ResultsAsync.ConfigureAwait(false)).FirstOrDefault();
                    }

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

        [HttpPost("GetPerson")]
        public async Task<ApiBaseModel> GetPerson(ReqGetPerson request)
        {
            try
            {
                using (var client = new BoltGraphClient(neu4jUri, neu4jUser, neu4jPwd))
                {
                    await client.ConnectAsync().ConfigureAwait(false);

                    var query = client.Cypher.WithParams(request);
                    query = query.Match("(aa:person)");
                    query = query.Where("1 = 1");

                    if (request.personIdList != null && request.personIdList.Count() > 0)
                    {
                        request.personIdList = request.personIdList.Distinct().ToList();
                        query = query.AndWhere((ReqGetPerson aa) => aa.personId.In(request.personIdList));
                    }

                    if (!string.IsNullOrEmpty(request.personId))
                    {
                        query = query.AndWhere((ReqGetPerson aa) => aa.personId == request.personId);
                    }

                    query = query.AndWhere("(aa.status < 95 OR aa.status IS NULL)");

                    IEnumerable<dynamic> dataList;
                    int dataTotalCount = 0;

                    if (!request.pageNumber.HasValue || !request.pageSize.HasValue)
                    {
                        dataList = await query.Return((aa) => new
                        {
                            personId = aa.As<ResGetPerson>().personId,
                            name = aa.As<ResGetPerson>().name,
                        }).ResultsAsync.ConfigureAwait(false);

                        dataTotalCount = dataList.Count();
                    }
                    else
                    {
                        dataList = await query.Return((aa) => new
                        {
                            personId = aa.As<ResGetPerson>().personId,
                            name = aa.As<ResGetPerson>().name,
                        })
                        .Skip((request.pageNumber - 1) * request.pageSize)
                        .Limit(request.pageSize)
                        .ResultsAsync.ConfigureAwait(false);

                        // 資料總數量
                        dataTotalCount = (await query
                                         .Return(() => Neo4jClient.Cypher.Return.As<int>("COUNT(DISTINCT aa)"))
                                         .ResultsAsync.ConfigureAwait(false)).FirstOrDefault();
                    }

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

        [HttpPost("SendMail")]
        public async Task<object> SendMail(ReqSendMail request)
        {
            try
            {
                string smtpAddress = "smtp.office365.com";
                string smtpSender = "system@hannlync.com";
                string smtpPwd = "Qub73342";
                int smtpPort = 587;
                bool ssl = true;

                string subject = "You have 1 application of WFA.";
                string body = string.Empty;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Please go to NetMatrix or press the link below to process the application.");
                sb.AppendLine("https://netmatrix.dev.hannlync.com/");
                body = sb.ToString();

                // 顯示在郵件上的 Sender
                string senderInfo = "hannlync";

                using (MimeMessage mail = new MimeMessage())
                {
                    // 使用 BodyBuilder 建立郵件內容
                    var bodyBuilder = new BodyBuilder();

                    mail.From.Add(new MailboxAddress("hannlync", smtpSender));    //寄件者名稱, 寄件者信箱

                    List<string> mailList = new List<string>();

                    foreach (var emailString in request.recipientEmail.Split(','))
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(emailString))
                            {
                                string emailReplace = emailString.Replace(" ", "");

                                if (emailReplace.IndexOf("@") > 0)
                                {
                                    mail.To.Add(new MailboxAddress(emailReplace, emailReplace));  //收件者名稱, 收件者信箱
                                    mailList.Add(emailReplace);
                                }
                            }
                        }
                        catch
                        {
                            logger.Error($@"Email error: {request.recipientEmail}");
                        }
                    }

                    mail.Subject = subject;
                    bodyBuilder.TextBody = body;

                    // 設定郵件內容
                    mail.Body = bodyBuilder.ToMessageBody();

                    //失敗重試連線 MailServer 次數、間隔
                    int retryCountMax = 3;
                    int retryIntervalMilliSec = 3000;

                    //失敗重試連線 MailServer
                    for (int runCount = 0; runCount < retryCountMax; runCount++)
                    {
                        try
                        {
                            using (var client = new MailKit.Net.Smtp.SmtpClient())
                            {
                                client.Timeout = 10000;
                                if (ssl)
                                {
                                    await client.ConnectAsync(smtpAddress, smtpPort, SecureSocketOptions.StartTls).ConfigureAwait(false);
                                }
                                else
                                {
                                    await client.ConnectAsync(smtpAddress, smtpPort).ConfigureAwait(false);
                                }
                                await client.AuthenticateAsync(smtpSender, smtpPwd).ConfigureAwait(false);

                                await client.SendAsync(mail).ConfigureAwait(false);
                                await client.DisconnectAsync(true).ConfigureAwait(false);
                            }

                            if (runCount > 0)
                            {
                                logger.Warn($"SendMail 重試 {runCount} 次才成功");
                            }

                            break;
                        }
                        catch (Exception ex)
                        {
                            if (runCount == retryCountMax - 1)
                            {
                                throw;
                            }

                            await Task.Delay(retryIntervalMilliSec).ConfigureAwait(false);
                        }
                    }

                    apiResult.RetCode = 200;
                    apiResult.RetMsg = "Success";
                }
            }
            catch (Exception ex)
            {
                apiResult.RetCode = -1;
                apiResult.RetMsg = ex.Message;
                logger.Error(ex);
            }

            return apiResult;
        }

        public class ReqGetPerson
        {
            [Newtonsoft.Json.JsonProperty(PropertyName = "id")]
            public string? personId { get; set; }
            public List<string>? personIdList { get; set; }
            public int? pageNumber { get; set; }
            public int? pageSize { get; set; }
        }
        public class ResGetPerson : ReqGetPerson
        {
            public string? name { get; set; }
        }


        public class ReqSendMail
        {
            public string? recipientEmail { get; set; }
        }
        public class ReqGetUser
        {
            [Newtonsoft.Json.JsonProperty(PropertyName = "id")]
            public string? userId { get; set; }
            public List<string>? userIdList { get; set; }
            public int? pageNumber { get; set; }
            public int? pageSize { get; set; }
        }
        public class ResGetUser : ReqGetUser
        {
            public string? name { get; set; }
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