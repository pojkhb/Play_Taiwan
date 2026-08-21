using System;
using System.Linq;
using System.Text;

// using backend.Middleware;
// using backend.Middleware.jwt;
using backend.Services;
using backend.dao;
using backend.utils;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
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
            services.AddScoped<Services.TaskService>();
            services.AddScoped<dao.TaskDao>();
            services.AddScoped<Services.ITaskVerificationService, Services.TaskVerificationService>();

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
            #region S13-收藏
            services.AddScoped<Services.FavoriteService>();
            services.AddScoped<dao.FavoriteDao>();
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
            app.UseCors();
            app.UseStaticFiles();
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
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
