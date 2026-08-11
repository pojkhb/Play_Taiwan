using System;
using System.Collections.Generic;
using System.Text.Json;
using backend.Models;
using backend.utils;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class StoryDao
    {
        private readonly AppSettings _appSettings;

        public StoryDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        #region 來點遊意思：轉盤地區

        public StoryWheelSpinResponse WheelSpin()
        {
            using var connection = new MySqlConnection(_appSettings.mydb);

            connection.Open();

            string sql = @"
                SELECT
                    region_id,
                    region_name,
                    city_name,
                    district_name
                FROM md_region
                WHERE is_active = 1
                  AND is_wheel_enabled = 1
                ORDER BY RAND()
                LIMIT 1;
            ";

            using var command = new MySqlCommand(sql, connection);
            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                throw new Exception("目前沒有可用的轉盤地區資料。");
            }

            return new StoryWheelSpinResponse
            {
                region_id = reader["region_id"].ToString(),
                region = reader["region_name"].ToString(),
                city_name = reader["city_name"] == DBNull.Value
                    ? null
                    : reader["city_name"].ToString(),
                district_name = reader["district_name"] == DBNull.Value
                    ? null
                    : reader["district_name"].ToString()
            };
        }

        #endregion

        #region 現在揪出發：取得地區清單

        public List<StoryWheelSpinResponse> GetRegions(string mode)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);

            connection.Open();

            string sql = @"
                SELECT
                    region_id,
                    region_name,
                    city_name,
                    district_name
                FROM md_region
                WHERE is_active = 1
                  AND (
                        @mode <> 'NOW'
                        OR is_now_departure = 1
                  )
                ORDER BY sort_order;
            ";

            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@mode", mode ?? "");

            using var reader = command.ExecuteReader();

            var result = new List<StoryWheelSpinResponse>();

            while (reader.Read())
            {
                result.Add(new StoryWheelSpinResponse
                {
                    region_id = reader["region_id"].ToString(),
                    region = reader["region_name"].ToString(),
                    city_name = reader["city_name"] == DBNull.Value
                        ? null
                        : reader["city_name"].ToString(),
                    district_name = reader["district_name"] == DBNull.Value
                        ? null
                        : reader["district_name"].ToString()
                });
            }

            return result;
        }

        #endregion

        #region 劇本檔案館：取得劇本卡片

        public List<StoryOptionResponse> GenerateOptions(
            StoryGenerateRequest req
        )
        {
            using var connection = new MySqlConnection(_appSettings.mydb);

            connection.Open();

            string sql = @"
                SELECT
                    s.story_id,
                    s.title,
                    s.prologue,
                    s.category,
                    s.transport,
                    s.expected_badges_json,
                    s.expected_postcards,
                    s.region_id,
                    r.region_name
                FROM md_story s
                LEFT JOIN md_region r
                    ON s.region_id = r.region_id
                WHERE s.is_active = 1
                  AND (
                        @region_id = ''
                        OR s.region_id = @region_id
                  )
                  AND (
                        @region = ''
                        OR r.region_name LIKE CONCAT('%', @region, '%')
                  )
                ORDER BY s.sort_order, s.created_at;
            ";

            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue(
                "@region_id",
                req?.region_id?.Trim() ?? ""
            );

            command.Parameters.AddWithValue(
                "@region",
                req?.region?.Trim() ?? ""
            );

            using var reader = command.ExecuteReader();

            var result = new List<StoryOptionResponse>();

            while (reader.Read())
            {
                string expectedBadgesJson =
                    reader["expected_badges_json"] == DBNull.Value
                        ? "[]"
                        : reader["expected_badges_json"].ToString();

                List<string> expectedBadges;

                try
                {
                    expectedBadges =
                        JsonSerializer.Deserialize<List<string>>(
                            expectedBadgesJson
                        ) ?? new List<string>();
                }
                catch
                {
                    expectedBadges = new List<string>();
                }

                result.Add(new StoryOptionResponse
                {
                    story_id = reader["story_id"].ToString(),
                    title = reader["title"].ToString(),

                    prologue = reader["prologue"] == DBNull.Value
                        ? null
                        : reader["prologue"].ToString(),

                    category = reader["category"] == DBNull.Value
                        ? null
                        : reader["category"].ToString(),

                    transport = reader["transport"] == DBNull.Value
                        ? null
                        : reader["transport"].ToString(),

                    expected_badges = expectedBadges,

                    expected_postcards = Convert.ToInt32(
                        reader["expected_postcards"]
                    ),

                    region_id = reader["region_id"] == DBNull.Value
                        ? null
                        : reader["region_id"].ToString(),

                    region = reader["region_name"] == DBNull.Value
                        ? null
                        : reader["region_name"].ToString(),

                    route_preview = GetRoutePreview(
                        reader["story_id"].ToString()
                    )
                });
            }

            return result;
        }

        #endregion

        #region 劇本詳情

        public StoryDetailResponse GetDetail(string storyId)
        {
            if (string.IsNullOrWhiteSpace(storyId))
            {
                throw new Exception("story_id 不可為空白。");
            }

            using var connection = new MySqlConnection(_appSettings.mydb);

            connection.Open();

            string storySql = @"
                SELECT
                    s.story_id,
                    s.title,
                    s.subtitle,
                    s.prologue,
                    s.synopsis
                FROM md_story s
                WHERE s.story_id = @story_id
                  AND s.is_active = 1;
            ";

            using var storyCommand = new MySqlCommand(
                storySql,
                connection
            );

            storyCommand.Parameters.AddWithValue("@story_id", storyId);

            using var storyReader = storyCommand.ExecuteReader();

            if (!storyReader.Read())
            {
                throw new Exception("找不到此劇本：" + storyId);
            }

            var result = new StoryDetailResponse
            {
                story_id = storyReader["story_id"].ToString(),

                title = storyReader["title"].ToString(),

                subtitle = storyReader["subtitle"] == DBNull.Value
                    ? ""
                    : storyReader["subtitle"].ToString(),

                synopsis = storyReader["synopsis"] == DBNull.Value
                    ? storyReader["prologue"]?.ToString()
                    : storyReader["synopsis"].ToString(),

                route_nodes = new List<StoryOptionResponse.RouteNode>()
            };

            storyReader.Close();

            string nodeSql = @"
                SELECT
                    n.node_id,
                    n.node_order,
                    COALESCE(p.place_name, n.node_title) AS location_name
                FROM md_story_node n
                LEFT JOIN md_place p
                    ON n.place_id = p.place_id
                WHERE n.story_id = @story_id
                  AND n.is_active = 1
                ORDER BY n.node_order;
            ";

            using var nodeCommand = new MySqlCommand(nodeSql, connection);

            nodeCommand.Parameters.AddWithValue("@story_id", storyId);

            using var nodeReader = nodeCommand.ExecuteReader();

            while (nodeReader.Read())
            {
                result.route_nodes.Add(
                    new StoryOptionResponse.RouteNode
                    {
                        node_id = nodeReader["node_id"].ToString(),

                        location_name =
                            nodeReader["location_name"].ToString(),

                        node_order = Convert.ToInt32(
                            nodeReader["node_order"]
                        )
                    }
                );
            }

            return result;
        }

        #endregion

        #region 劇本卡片路線預覽

        private List<string> GetRoutePreview(string storyId)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);

            connection.Open();

            string sql = @"
                SELECT
                    COALESCE(p.place_name, n.node_title) AS location_name
                FROM md_story_node n
                LEFT JOIN md_place p
                    ON n.place_id = p.place_id
                WHERE n.story_id = @story_id
                  AND n.is_active = 1
                ORDER BY n.node_order;
            ";

            using var command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@story_id", storyId);

            using var reader = command.ExecuteReader();

            var result = new List<string>();

            while (reader.Read())
            {
                result.Add(reader["location_name"].ToString());
            }

            return result;
        }

        #endregion
    }
}