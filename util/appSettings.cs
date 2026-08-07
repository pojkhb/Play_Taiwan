using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace backend.utils
{
  public class AppSettings
  {
    public int expires {get;set;}
    public string jwt_secret { get; set; }
    public string mydb { get; set; }
    public string aes_secret_key { get; set; }
    public string aes_secret_iv { get; set; }
    public string domain_name { get; set; }
    public string hash_key { get; set; }
    public string ct_code { get; set; }
    public string background_color {get;set;}
    public string header {get;set;}
  }
}