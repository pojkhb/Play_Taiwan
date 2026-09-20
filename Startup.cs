using System;
using System.Linq;
using System.Text;

// using backend.Middleware;
// using backend.Middleware.jwt;
using backend.Services;
using backend.Services.Neo4j;
using backend.dao;
using backend.utils;
using Neo4j.Driver;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace backend
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpContextAccessor();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            string[] corsOrigins = Configuration["Cors:AllowOrigin"].Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries
            );
            services.AddCors(options =>
            {
                options.AddDefaultPolicy(builder =>
                {
                    if (corsOrigins.Contains("*"))
                    {
                        builder.SetIsOriginAllowed(_ => true);
                    }
                    else
                    {
                        builder.WithOrigins(corsOrigins);
                    }
                    builder.AllowAnyMethod();
                    builder.AllowAnyHeader();
                    builder.AllowCredentials();
                });
            });

            // JWT
            // services
            //     .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            //     .AddJwtBearer(options =>
            //     {
            //         var ServerSecret = new SymmetricSecurityKey(
            //             Encoding.UTF8.GetBytes(Configuration["JWT:Key"])
            //         );
            //         options.TokenValidationParameters = new TokenValidationParameters
            //         {
            //             ValidateIssuer = true,
            //             ValidIssuer = Configuration["Jwt:Issuer"],
            //             ValidateAudience = true,
            //             ValidAudience = Configuration["Jwt:Issuer"],
            //             ValidateLifetime = false,
            //             ValidateIssuerSigningKey = true,
            //             IssuerSigningKey = new SymmetricSecurityKey(
            //                 Encoding.UTF8.GetBytes(Configuration["Jwt:Key"])
            //             )
            //         };
            //     });
            services.AddHttpClient();

            // 若以 IIS in-process 模式代管，Kestrel 的 [RequestSizeLimit] 不會生效，
            // 需另外調高 IIS 的請求大小上限，跟 UploadController 的 MaxUploadBytes 對齊（200MB）。
            services.Configure<IISServerOptions>(options =>
            {
                options.MaxRequestBodySize = 200 * 1024 * 1024;
            });

            #region S05-登入/探員帳號 (Auth)
            services.AddScoped<Services.AuthService>();
            services.AddScoped<dao.AuthDao>();
            services.AddScoped<EmailService>();
            #endregion
            #region S06-首頁總覽
            services.AddScoped<Services.HomeService>();
            services.AddScoped<dao.HomeDao>();
            #endregion
            #region S07-劇本生成 (RAG+LLM)
            services.AddScoped<Services.StoryService>();
            services.AddScoped<dao.StoryDao>();
            #endregion
            #region S08-地圖/節點/導航
            services.AddScoped<Services.MapService>();
            services.AddScoped<dao.MapDao>();
            #endregion
            #region S09-任務答題
            services.AddHttpClient<Services.Neo4jService>();
            services.AddScoped<Services.TaskService>();
            services.AddScoped<dao.TaskDao>();
            services.AddScoped<Services.ITaskVerificationService, Services.TaskVerificationService>();
            services.AddScoped<Services.TaskGenerationService>();

            services.AddSingleton<Services.IVisionApiClient, Services.FakeVisionApiClient>();
            services.AddSingleton<Services.IPoseCompareClient, Services.FakePoseCompareClient>();
            services.AddSingleton<Services.ISpeechToTextClient, Services.FakeSpeechToTextClient>();
            services.AddSingleton<Services.IQrTokenStore, Services.InMemoryQrTokenStore>();
            #endregion
            #region S10-明信片
            services.AddScoped<Services.PostcardService>();
            services.AddScoped<dao.PostcardDao>();
            #endregion
            #region S11-徽章
            services.AddScoped<Services.BadgeService>();
            services.AddScoped<dao.BadgeDao>();
            #endregion
            #region S12-過往紀錄
            services.AddScoped<Services.HistoryService>();
            services.AddScoped<dao.HistoryDao>();
            #endregion
            #region S14-剪影圖片
            services.AddScoped<Services.SilhouetteService>();
            services.AddScoped<dao.SilhouetteDao>();
            #endregion
            #region S15-明信片主檔
            services.AddScoped<Services.PostcardCatalogService>();
            services.AddScoped<dao.PostcardCatalogDao>();
            #endregion
            #region S16-任務線索提示
            services.AddScoped<Services.TaskHintService>();
            services.AddScoped<dao.TaskHintDao>();
            #endregion
            #region AI 非同步生成與任務追蹤
            services.AddScoped<MediaJobDao>();
            services.AddScoped<IVlogAiClient, MockVlogAiClient>();

            // AI 任務生成 API（對應 AI_Task_API_Spec.md）
            // AiTaskClient 需要注入 HttpClient，先在這裡註冊好逾時設定（規格書建議 30 秒），
            // 之後正式串接時只需把下面 MockAiTaskClient 改成 AiTaskClient 這一行即可，不會再卡 DI 解析失敗。
            services.AddHttpClient<Services.AiTaskClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            // 開發/測試時使用 MockAiTaskClient（不呼叫實際 AI 服務）
            // 正式上線後改為 services.AddScoped<IAiTaskClient, AiTaskClient>();
            services.AddScoped<IAiTaskClient, MockAiTaskClient>();
            #endregion
            #region S08-地圖/節點/導航
            services.AddScoped<Services.MapService>();
            services.AddScoped<dao.MapDao>();
            services.AddScoped<Services.GeocodingService>();
            #endregion
            services.AddScoped<VisitorVlogService>();
            services.AddScoped<VisitorVlogDao>();

            #region S17-商家資料維護 + NFC（play_taiwan_db_v4：auth/store/coupon/nfc_coupon/user_coupon/store_question/question_option）
            services.Configure<Neo4jSettings>(Configuration.GetSection("Neo4jSettings"));

            // Neo4j:Mode = Local（本地測試 Driver）/ Remote（正式對外 /api/neo4j/cypher）
            // 上層 PlaceVersionChainService 只依賴 INeo4jGatewayService，切換這裡即可，不用改業務邏輯。
            bool useRemoteNeo4j = Configuration["Neo4j:Mode"] == "Remote";
            if (useRemoteNeo4j)
            {
                services.AddHttpClient<RemoteNeo4jApiGatewayService>(client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                });
                services.AddScoped<INeo4jGatewayService>(sp => sp.GetRequiredService<RemoteNeo4jApiGatewayService>());
            }
            else
            {
                services.AddSingleton<IDriver>(sp =>
                {
                    Neo4jSettings config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Neo4jSettings>>().Value;
                    return GraphDatabase.Driver(config.Uri, AuthTokens.Basic(config.User, config.Password));
                });
                services.AddScoped<INeo4jGatewayService, LocalNeo4jDriverGatewayService>();
            }

            services.AddScoped<PlaceVersionChainService>();
            services.AddScoped<ICurrentActorProvider, RequestActorProvider>();

            services.AddScoped<Services.MerchantAccountService>();
            services.AddScoped<dao.MerchantAccountDao>();
            services.AddScoped<Services.MerchantCouponService>();
            services.AddScoped<dao.MerchantCouponDao>();
            services.AddScoped<Services.MerchantNfcService>();
            services.AddScoped<dao.MerchantNfcDao>();
            services.AddScoped<Services.MerchantQuestionService>();
            services.AddScoped<dao.MerchantQuestionDao>();
            #endregion
            // JWT Authorize
            // services.AddScoped<JWTUserService>();
            // services.AddScoped<JWTDao>();
            // services.AddScoped<RoleProcessService>();
            // services.AddScoped<RoleProcessDao>();

            services
                .AddControllers()
                .AddJsonOptions(
                    options => options.JsonSerializerOptions.PropertyNamingPolicy = null
                );

            services.Configure<AppSettings>(Configuration.GetSection("AppSettings"));

            var jwtSecret = Configuration["AppSettings:jwt_secret"];

            if (string.IsNullOrWhiteSpace(jwtSecret))
            {
                throw new InvalidOperationException(
                    "找不到 AppSettings:jwt_secret，請確認 appsettings.Development.json。"
                );
            }

            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(options =>
                {
                    options.RequireHttpsMetadata = false;

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(jwtSecret)
                        ),

                        ValidateIssuer = false,
                        ValidateAudience = false,

                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero
                    };
                });

            services.AddAuthorization();

            services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Play Taiwan API",
        Version = "v1"
    });

    // ↓↓↓ 新增這 3 行 ↓↓↓
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);
    // ↑↑↑ 新增這 3 行 ↑↑↑

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "貼上 JWT Token，Swagger 會自動加入 Bearer 前綴。"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

            services.AddMvc();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // 集中式例外處理放在最外層：包住包含 DeveloperExceptionPage 在內的所有後續中介軟體，
            // 讓 API 不管在哪個環境，未攔截例外都統一回傳 ResultViewModel JSON，
            // 不會在本機開發時被 DeveloperExceptionPage 攔走、變成整頁 HTML 除錯畫面。
            app.UseMiddleware<backend.Middleware.ExceptionHandlingMiddleware>();

            app.UseCors();
            app.UseStaticFiles();
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
                c.RoutePrefix = "";
            });

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            /* 中介軟體 */
            // app.UseMiddleware<jwtMiddleware>();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            app.UseForwardedHeaders(
                new ForwardedHeadersOptions
                {
                    ForwardedHeaders =
                        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                }
            );
        }
    }
}
