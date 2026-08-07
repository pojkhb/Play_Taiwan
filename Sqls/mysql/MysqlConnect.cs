using System;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using System.Data.SqlClient;
using System.Collections;
using MySql.Data.MySqlClient;
using backend.utils;

using Microsoft.Extensions.Options;
using backend.Extensions;

namespace backend.Sqls.mysql
{
  public class MysqlConnect
  {
    private readonly string _cnstr;
    private readonly MySqlConnection _conn;
    private readonly MysqlDataAccess _access;

    public MysqlConnect(string cnstr)
    {
      _cnstr = cnstr;
      _conn = new MySqlConnection(_cnstr);
      _access = new MysqlDataAccess();
    }

    public DataTable GetDataTable(string sql)
    {
      DataTable Result = _access.GetDataTable(_conn, sql);
      return Result;
    }
    public DataTable GetDataTable(string sql, Hashtable ht)
    {
      DataTable Result = _access.GetDataTable(_conn, sql, ht);
      return Result;
    }

    public List<T> GetDataList<T>(string sql) where T : new()
    {
      return (List<T>)DataTableExtension.ToList<T>(GetDataTable(sql));
    }
    public List<T> GetDataList<T>(string sql, Hashtable ht) where T : new()
    {
      return (List<T>)DataTableExtension.ToList<T>(GetDataTable(sql, ht));
    }

    public void Execute(string sql, Hashtable ht)
    {
      _access.Execute(_conn, sql, ht);
    }
  }
}