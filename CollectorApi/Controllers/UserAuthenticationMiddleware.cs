using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace CollectorApi.Controllers
{
    public class UserAuthenticationMiddleware
    {
        private readonly IConfiguration _configuration;
        private readonly RequestDelegate _next;
        private readonly IHttpClientFactory _httpClientFactory;
        private string? BaseAgw4web1Url;
        protected static Logger logger { get; private set; }

        public UserAuthenticationMiddleware(RequestDelegate next, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _next = next;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;

            BaseAgw4web1Url = _configuration["BaseAgw4web1Url"];

            logger = LogManager.GetLogger(GetType().FullName);
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 檢查 AllowAnonymous
            var endpoint = context.GetEndpoint();
            if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null)
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            // 只對 controller 做身份驗證
            // if (context.Request.Path.StartsWithSegments("/api"))
            if (context.Request.Path.StartsWithSegments("/api/AeonMatrix/AddOrUpdWfaConfig")) // 只驗證 AddOrUpdWfaConfig
            {
                try
                {
                    bool continueProcessing = await UserAuthentication(context.Request.Cookies, context).ConfigureAwait(false);
                    if (!continueProcessing)
                    {
                        return;
                    }

                    // 修改请求内容
                    var modifyBody = await ModifyRequestBody(context).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.Error(ex);
                }

            }

            // 繼續執行下一個中間件
            await _next(context).ConfigureAwait(false);
        }

        /// <summary>
        /// 身份驗證
        /// </summary>
        /// <returns></returns>
        private async Task<bool> UserAuthentication(IRequestCookieCollection cookie, HttpContext context)
        {
            bool isSuccessStatusCode = false;

            var client = _httpClientFactory.CreateClient("UserAuthentication");
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{BaseAgw4web1Url}/user/me");

            List<string> cookieDataList = new List<string>();
            foreach (var cookieData in cookie)
            {
                cookieDataList.Add($"{cookieData.Key}={cookieData.Value}");
            }
            if (cookieDataList.Count > 0)
            {
                requestMessage.Headers.Add("Cookie", string.Join("; ", cookieDataList));
            }

            try
            {
                var response = await client.SendAsync(requestMessage).ConfigureAwait(false);

                // 回傳錯誤
                if (!response.IsSuccessStatusCode)
                {
                    context.Response.StatusCode = (int)response.StatusCode;
                    string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    await context.Response.WriteAsync(content).ConfigureAwait(false);
                    await context.Response.CompleteAsync().ConfigureAwait(false);
                }
                else
                {
                    var userData = JsonConvert.DeserializeObject<dynamic>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    context.Items["UserData"] = userData;

                    isSuccessStatusCode = true;
                }

            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync($"{ex.ToString()}").ConfigureAwait(false);
                await context.Response.CompleteAsync().ConfigureAwait(false);
                logger.Error(ex);
            }

            return isSuccessStatusCode;
        }

        /// <summary>
        /// 寫入 crt_id, upd_id
        /// </summary>
        /// <returns></returns>
        private async Task<bool> ModifyRequestBody(HttpContext context)
        {
            bool isSuccess = false;
            try
            {
                context.Request.EnableBuffering();

                // 讀取 request body
                string requestBody;
                using (var reader = new StreamReader(context.Request.Body, encoding: System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
                {
                    requestBody = await reader.ReadToEndAsync().ConfigureAwait(false);
                    context.Request.Body.Position = 0;  // 重置流位置
                }

                var token = JToken.Parse(requestBody);
                dynamic userData = context.Items["UserData"];

                // 修改 request 內容
                if (token.Type == JTokenType.Array)
                {
                    JArray array = JArray.Parse(requestBody);
                    foreach (JObject obj in array)
                    {
                        UpdateRequestData(obj, userData);
                    }
                    requestBody = JsonConvert.SerializeObject(array);
                }
                else
                {
                    JObject obj = JObject.Parse(requestBody);
                    UpdateRequestData(obj, userData);
                    requestBody = JsonConvert.SerializeObject(obj);
                }

                var modifiedStream = new MemoryStream();
                using (var writer = new StreamWriter(modifiedStream, System.Text.Encoding.UTF8, 1024, leaveOpen: true))
                {
                    await writer.WriteAsync(requestBody).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                }
                modifiedStream.Position = 0;

                context.Request.Body = modifiedStream;

                isSuccess = true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return isSuccess;

            void UpdateRequestData(JObject obj, dynamic userData)
            {
                if (obj != null && userData != null)
                {
                    obj["crt_id"] = userData.personId;
                    obj["upd_id"] = userData.personId;
                }
            }
        }
    }
}