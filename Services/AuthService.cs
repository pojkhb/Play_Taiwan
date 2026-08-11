using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using backend.dao;
using backend.Models;
using backend.utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace backend.Services
{
    public class AuthService
    {
        private readonly AuthDao _dao;
        private readonly HttpContext _ipContext;
        private readonly AppSettings _appSettings;

        public AuthService(AuthDao dao, IHttpContextAccessor httpContextAccessor, IOptions<AppSettings> appSettings)
        {
            _dao = dao;
            _ipContext = httpContextAccessor.HttpContext;
            _appSettings = appSettings.Value;
        }

        #region 登入-探員代號+通行密碼
        public LoginResponse Login(LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.ep_id) ||
                string.IsNullOrWhiteSpace(req.ep_pswd))
            {
                throw new Exception("請輸入探員代號與通行密碼");
            }

            EpAccount account = _dao.GetEpAccount(req.ep_id);

            if (account == null)
            {
                throw new Exception("帳號或密碼錯誤");
            }

            if (!account.is_active)
            {
                throw new Exception("此帳號已停用");
            }

            sha256Hash hashTool = new sha256Hash();

            string inputPasswordHash = hashTool.getSha256(
                req.ep_pswd,
                _appSettings.hash_key
            );

            if (inputPasswordHash != account.ep_pswd)
            {
                throw new Exception("帳號或密碼錯誤");
            }

            string token = GenerateJwtToken(account);

            return new LoginResponse
            {
                token = token,
                ep_id = account.ep_id,
                ep_name = account.ep_name
            };
        }
        #endregion
        /// <summary>
        /// 產生探員登入 JWT Token。
        /// </summary>
        private string GenerateJwtToken(EpAccount account)
        {
            SymmetricSecurityKey securityKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_appSettings.jwt_secret)
            );

            SigningCredentials credentials = new SigningCredentials(
                securityKey,
                SecurityAlgorithms.HmacSha256
            );

            Claim[] claims = new[]
            {
        new Claim("ep_id", account.ep_id),
        new Claim("ep_name", account.ep_name),
        new Claim(ClaimTypes.NameIdentifier, account.ep_id),
        new Claim(ClaimTypes.Name, account.ep_name)
    };

            JwtSecurityToken token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_appSettings.expires),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        #region 登出
        public void Logout()
        {
            // TODO: 若有 refresh token / session 表，於此處撤銷
        }
        #endregion

        #region 取得探員帳號資訊
        public LoginResponse GetProfile()
        {
            return _dao.GetProfile();
        }
        #endregion

        #region 編輯探員帳號名稱
        public void UpdateProfile(EpAccountUpdateRequest req)
        {
            _dao.UpdateProfile(req);
        }
        #endregion
    }
}