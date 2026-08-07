using System;
// using System.Data;
using MySql.Data.MySqlClient;


namespace backend.Sqls.mysql
{
  [Serializable]
  public class MySQLParameter
  {
    public MySQLParameter(object ObjValue, MySqlDbType SqlDbType)
    {
      this.ObjValue = ObjValue;
      this.SqlDbType = SqlDbType;
    }

    object _objValue;

    public object ObjValue
    {
      get { return _objValue; }
      set
      {
        if ((value == null))
          _objValue = DBNull.Value;
        else _objValue = value;
      }
    }

    MySqlDbType _SqlDbType;

    public MySqlDbType SqlDbType
    {
      get { return _SqlDbType; }
      set { _SqlDbType = value; }
    }
  }
}