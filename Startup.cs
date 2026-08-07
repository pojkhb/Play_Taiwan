using System;
using System.Linq;
using System.Text;

using backend.Middleware;
using backend.Middleware.jwt;
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
            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    var ServerSecret = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(Configuration["JWT:Key"])
                    );
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = Configuration["Jwt:Issuer"],
                        ValidateAudience = true,
                        ValidAudience = Configuration["Jwt:Issuer"],
                        ValidateLifetime = false,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(Configuration["Jwt:Key"])
                        )
                    };
                });

            #region 共用涵式
                services.AddScoped<Services.SharedFunctionService>();
                services.AddScoped<dao.SharedFunctionDao>();
            #endregion

            #region 框架功能
                services.AddScoped<Services.FrameFunctionService>();
                services.AddScoped<dao.FrameFunctionDao>();
            #endregion 

            #region S01-帳號管理
                services.AddScoped<Services.OperatorSettingService>();
                services.AddScoped<dao.OperatorSettingDao>();
            #endregion
            #region S02-角色權限設定
                services.AddScoped<Services.RoleModuleSettingService>();
                services.AddScoped<dao.RoleModuleSettingDao>();
            #endregion
            #region S03-模組設定
                services.AddScoped<Services.ModuleSettingService>();
                services.AddScoped<dao.ModuleSettingDao>();
            #endregion
            #region S04-帳號申請
                services.AddScoped<Services.OperatorApplyService>();
                services.AddScoped<dao.OperatorApplyDao>();
            #endregion
            #region 忘記密碼
                services.AddScoped<Services.ForgotPasswordService>();
                services.AddScoped<dao.ForgotPasswordDao>();
            #endregion
            #region 歷史紀錄
                services.AddScoped<Services.LogService>();
                services.AddScoped<dao.LogDao>();
            #endregion

            // JWT Authorize
            services.AddScoped<JWTUserService>();
            services.AddScoped<JWTDao>();
            services.AddScoped<RoleProcessService>();
            services.AddScoped<RoleProcessDao>();

            services
                .AddControllers()
                .AddJsonOptions(
                    options => options.JsonSerializerOptions.PropertyNamingPolicy = null
                );

            services.Configure<AppSettings>(Configuration.GetSection("AppSettings"));

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "backend", Version = "v1" });
            });
            services.AddMvc();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseCors();

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
            /* 中介軟體 */
            app.UseMiddleware<jwtMiddleware>();
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
