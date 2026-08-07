using System;
using System.Drawing;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace backend.utils
{
    public class convertType
    {
        public static string Base64ToImageSave(string base64, string dir, string fileName)
        {
            string Result = $@"/Image/{dir}/{fileName}";
            if (!Directory.Exists($@"./Image"))
            {
                Directory.CreateDirectory($@"./Image");
            }
            if (!Directory.Exists($@"./Image/{dir}"))
            {
                Directory.CreateDirectory($@"./Image/{dir}");
            }
            File.WriteAllBytes($@"./{Result}", Convert.FromBase64String(base64));
            return Result;
        }

        public static string iFormFileToBase64(IFormFile file)
        {
            string Result = string.Empty;
            if (file.Length > 0)
            {
                using (var ms = new MemoryStream())
                {
                    file.CopyTo(ms);
                    var fileBytes = ms.ToArray();
                    Result = Convert.ToBase64String(fileBytes);
                }
            }
            return Result;
        }
        public static string iFormFileSave(IFormFile file, Guid fileid)
        {
            string base64 = string.Empty;
            if (file.Length > 0)
            {
                using (var ms = new MemoryStream())
                {
                    file.CopyTo(ms);
                    var fileBytes = ms.ToArray();
                    base64 = Convert.ToBase64String(fileBytes);
                }
            }
            string dir = fileid.ToString();
            string fileName = file.FileName;
            string Result = $@"/Image/{dir}/{fileName}";
            if (!Directory.Exists($@"./Image"))
            {
                Directory.CreateDirectory($@"./Image");
            }
            if (!Directory.Exists($@"./Image/{dir}"))
            {
                Directory.CreateDirectory($@"./Image/{dir}");
            }
            File.WriteAllBytes($@"./{Result}", Convert.FromBase64String(base64));
            return Result;
        }

        public static string iFormFileSave(IFormFile file, string fileid)
        {
            string base64 = string.Empty;
            if (file.Length > 0)
            {
                using (var ms = new MemoryStream())
                {
                    file.CopyTo(ms);
                    var fileBytes = ms.ToArray();
                    base64 = Convert.ToBase64String(fileBytes);
                }
            }
            string dir = fileid;
            string fileName = file.FileName;
            string Result = $@"/Image/{dir}/{fileName}";
            if (!Directory.Exists($@"./Image"))
            {
                Directory.CreateDirectory($@"./Image");
            }
            if (!Directory.Exists($@"./Image/{dir}"))
            {
                Directory.CreateDirectory($@"./Image/{dir}");
            }
            File.WriteAllBytes($@"./{Result}", Convert.FromBase64String(base64));
            return Result;
        }
    }
}