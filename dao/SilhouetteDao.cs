using System;
using System.Collections.Generic;
using backend.Models;
using backend.utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class SilhouetteDao
    {
        private readonly AppSettings _appSettings;

        public SilhouetteDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        #region 取得剪影清單

        public List<Silhouette> GetSilhouettes()
        {
            const string sql = @"
                SELECT
                    silhouette_id,
                    name,
                    image_url,
                    city,
                    category,
                    is_active,
                    sort_order
                FROM md_silhouette
                WHERE is_active = 1
                ORDER BY sort_order, name;
            ";

            var result = new List<Silhouette>();

            using var connection = new MySqlConnection(_appSettings.mydb);
            using var command = new MySqlCommand(sql, connection);

            connection.Open();

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                result.Add(new Silhouette
                {
                    silhouette_id = reader["silhouette_id"].ToString(),
                    name = reader["name"].ToString(),
                    image_url = reader["image_url"].ToString(),

                    city = reader["city"] == DBNull.Value
                        ? null
                        : reader["city"].ToString(),

                    category = reader["category"] == DBNull.Value
                        ? null
                        : reader["category"].ToString(),

                    is_active = Convert.ToBoolean(reader["is_active"]),
                    sort_order = Convert.ToInt32(reader["sort_order"])
                });
            }

            return result;
        }

        #endregion

        #region 取得单一剪影

        public Silhouette GetSilhouetteById(string silhouetteId)
        {
            const string sql = @"
                SELECT
                    silhouette_id,
                    name,
                    image_url,
                    city,
                    category,
                    is_active,
                    sort_order
                FROM md_silhouette
                WHERE silhouette_id = @silhouette_id
                  AND is_active = 1
                LIMIT 1;
            ";

            using var connection = new MySqlConnection(_appSettings.mydb);
            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@silhouette_id", silhouetteId);

            connection.Open();

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return null;
            }

            return new Silhouette
            {
                silhouette_id = reader["silhouette_id"].ToString(),
                name = reader["name"].ToString(),
                image_url = reader["image_url"].ToString(),

                city = reader["city"] == DBNull.Value
                    ? null
                    : reader["city"].ToString(),

                category = reader["category"] == DBNull.Value
                    ? null
                    : reader["category"].ToString(),

                is_active = Convert.ToBoolean(reader["is_active"]),
                sort_order = Convert.ToInt32(reader["sort_order"])
            };
        }

        #endregion
    }
}
