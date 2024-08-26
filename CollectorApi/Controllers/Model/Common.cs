using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static CollectorApi.Controllers.Collector.HttpController;
using static CollectorApi.Controllers.Collector.SnmpController;

namespace CollectorApi.Controllers.Model
{
    public class Common
    {
        public class SendDataModel
        {
            public string? CollectorToken { get; set; }
            public List<WaaResultModel>? HttpLogDataList { get; set; }
            public List<SnmpTaskInfo>? SnmpLogDataList { get; set; }
        }
    }
}