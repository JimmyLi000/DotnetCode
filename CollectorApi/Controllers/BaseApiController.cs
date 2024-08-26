using Microsoft.AspNetCore.Mvc;
using NLog;

namespace CollectorApi.Controllers
{
    public class BaseApiController : ControllerBase
    {
        protected static Logger logger { get; private set; }


        protected BaseApiController()
        {
            logger = LogManager.GetLogger(GetType().FullName);
            apiResult.ReqTime = DateTime.Now;
        }

        protected dynamic UserData
        {
            get
            {
                return HttpContext.Items["UserData"] ?? null;
            }
        }

        public enum RetCode : int
        {
            Ok = 200,
            Running = 1,
            Error = -1,
        }

        protected ApiBaseModel apiResult = new ApiBaseModel()
        {
            RetCode = 1,//RetCode.Running,
            ReqTime = DateTime.Now
        };

        protected void AfterProcess()
        {
            apiResult.RespTime = DateTime.Now;
            apiResult.RetMsg = string.IsNullOrEmpty(apiResult.RetMsg) && apiResult.RetCode.Equals(200) ? "Success" : apiResult.RetMsg;
            TimeSpan ts = DateTime.Now - apiResult.ReqTime;
            apiResult.Duration = $"{Convert.ToInt32(ts.TotalMilliseconds)} ms";
        }
    }

    public class ApiBaseModel
    {
        public int? RetCode { get; set; }
        public String? RetMsg { get; set; }
        public int RetNumber { get; set; }
        public DateTime ReqTime { get; set; }
        public DateTime RespTime { get; set; }

        //執行時間
        public string? Duration { get; set; }
        public object? ResultSet { get; set; }
    }
}
