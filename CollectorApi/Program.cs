using Microsoft.AspNetCore.Mvc;
using NLog;

Logger logger = LogManager.GetCurrentClassLogger();
logger.Info("***************************");
logger.Info("***                     ***");
logger.Info("***  Application start  ***");
logger.Info("***                     ***");
logger.Info("***************************");

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;
List<string> setIsOriginAllowed = configuration.GetSection("SetIsOriginAllowed").Value.Split(',').ToList();

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(5064);

    serverOptions.ListenAnyIP(5443, listenOptions =>
    {
        if (File.Exists("./hannlync.pfx"))
        {
            listenOptions.UseHttps("./hannlync.pfx", "12345678");
            logger.Info("credentials bound success");
        }
        else
        {
            logger.Warn("No credentials bound");
        }
    });
});

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    // 不檢查 request 裡的屬性e
    options.SuppressModelStateInvalidFilter = true;
});

// Add services to the container.
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
    {
        policy.SetIsOriginAllowed(origin => setIsOriginAllowed.FindIndex(x => origin.Contains(x)) > -1) // 允許來源
                                                                                                        //   .WithOrigins(corsOrigins)  // 允許來源
              .WithMethods("*")  // 允許所有HTTP方法
              .WithHeaders("*") // 允許所有標頭
              .AllowCredentials();
    });
});

// timer
builder.Services.AddHostedService<CollectorApi.Service.TimedHostedService>();

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("UserAuthentication", client =>
    {
        // HttpClient 基礎設定
    })
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        };
    });

var app = builder.Build();

// 使用 CORS 策略
app.UseCors("CorsPolicy");

// 身份驗證
app.UseMiddleware<CollectorApi.Controllers.UserAuthenticationMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();
app.MapHub<CollectorApi.WebSocket.ApiWebSocketHub>("/ApiWebSocketHub");

app.Run();

// docker build -t httpcollector .
// docker images
// docker-compose up

// curl 'http://10.88.21.196:5064/api/Snmp/ScanSnmpDevice' \
// -H 'Accept: application/json, text/plain, */*' \
// -H 'Accept-Language: zh-TW,zh;q=0.9,en-US;q=0.8,en;q=0.7' \
// -H 'Content-Type: application/json' \
// --data-raw '{
// "StartIp": "10.88.21.1",
// "EndIp": "10.88.21.254",
// "Community": "public",
// "Port": 161,
// "GetTraffic": 90
// }
// ' \
// --compressed


// curl 'http://10.88.21.196:5064/api/Http/ScanHttp' \
// -H 'Accept: application/json, text/plain, */*' \
// -H 'Accept-Language: zh-TW,zh;q=0.9,en-US;q=0.8,en;q=0.7' \
// -H 'Content-Type: application/json' \
// --data-raw '{
// "FQDN": "http://www.google.com",
// "HttpMethod": 10,
// "HttpContent": null,
// "HttpHeader": null
// }
// ' \
// --compressed

// CREATE (n1:syscode {codeGroup: "assetConnectType", codeName: "Ethernet", codeNo: 10, id: "55a1c8b3-a356-4872-822a-781ba2544bd9"})