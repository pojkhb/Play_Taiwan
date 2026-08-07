using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;

namespace backend.utils
{
    class IsNumber
    {
        /// <summary>
        /// 判斷字串是否為數字
        /// </summary>
        /// <param name="_numString"></param>
        /// <returns></returns>
        public static bool Checked(string _numString)
        {
            string numString = _numString;
            bool[] canConvert = new bool[3];
            long number1 = 0;
            byte number2 = 0;
            decimal number3 = 0;

            //嘗試numString轉換成三種數值型態
            canConvert[0] = long.TryParse(numString, out number1);
            canConvert[1] = byte.TryParse(numString, out number2);
            canConvert[2] = decimal.TryParse(numString, out number3);

            for (int i = 0; i < canConvert.Length; i++)
            {
                //如果三種數值型態其中一個可以正確轉換，就表示該字串為數值
                if (canConvert[i] == true)
                    return true;
            }

            return false;
        }
    }

}