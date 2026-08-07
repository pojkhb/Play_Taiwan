using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;

namespace backend.utils
{
    public class tokenEnCode
    {
        private readonly HttpContext _HttpContext;
        private HttpRequest _Request;
        private string _Token;

        public tokenEnCode(HttpContext HttpContext)
        {
            this._HttpContext = HttpContext ??
                throw new ArgumentNullException(nameof(HttpContext));

            this._Request = this._HttpContext.Request;
            this._Token = _Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
        }

        public Dictionary<string, object> GetHeader()
        {
            string[] JwtArr = (this._Token != null) ? this._Token.Split('.') : null;
            var Header = (JwtArr != null) ? JsonConvert.DeserializeObject<Dictionary<string, object>>(Base64UrlEncoder.Decode(JwtArr[0])) : null;

            return Header;
        }

        public Dictionary<string, object> GetPayLoad()
        {
            try
            {
                string[] JwtArr = (this._Token != null) ? this._Token.Split('.') : null;
                var PayLoad = (JwtArr != null) ? JsonConvert.DeserializeObject<Dictionary<string, object>>(Base64UrlEncoder.Decode(JwtArr[1])) : null;
                return PayLoad;
            }
            catch
            {
                return null;
            }
        }
    }
}