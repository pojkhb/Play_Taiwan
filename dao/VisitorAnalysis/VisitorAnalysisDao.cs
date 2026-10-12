// 檔案路徑：dao\VisitorAnalysis\VisitorAnalysisDao.cs
// 資料來源：獨立 MySQL 資料庫 `visitor_analysis`
// （attraction / domestic_analysis / inbound_analysis），與 play_taiwan_db 分開。
using System.Collections.Generic;
using System.Linq;
using Dapper;
using backend.utils;
using backend.Models;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class VisitorAnalysisDao
    {
        private readonly AppSettings _appSettings;

        public VisitorAnalysisDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        /// <summary>
        /// 連到 visitor_analysis。若 AppSettings 有 visitor_analysis_db 就用它，
        /// 否則沿用 mydb 的主機／帳密，只把 Database 改成 visitor_analysis。
        /// </summary>
        private MySqlConnection Open()
        {
            string connectionString = _appSettings.mydb;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                var builder = new MySqlConnectionStringBuilder(_appSettings.mydb)
                {
                    Database = "visitor_analysis"
                };
                connectionString = builder.ConnectionString;
            }

            return new MySqlConnection(connectionString);
        }

        private const string DomesticColumns = @"
            CAST(attraction_uid AS CHAR) AS attraction_uid,
            n_trips,
            weighted_trips,
            years,
            CAST(n_years AS SIGNED) AS n_years,
            CAST(first_year AS SIGNED) AS first_year,
            CAST(last_year AS SIGNED) AS last_year,
            mean_days,
            mean_nights,
            main_age,
            main_age_pct,
            male_pct,
            female_pct,
            main_purpose,
            main_purpose_pct,
            purpose_leisure_pct,
            purpose_business_pct,
            purpose_vfr_pct,
            top_transports,
            top_activities,
            CAST(cluster_id AS SIGNED) AS cluster_id,
            cluster_name,
            CAST(in_cluster AS SIGNED) AS in_cluster,
            ff_visitors_sum,
            ff_visitors_mean,
            CAST(ff_years AS SIGNED) AS ff_years
        ";

        private const string InboundColumns = @"
            CAST(attraction_uid AS CHAR) AS attraction_uid,
            n_trips,
            years,
            CAST(n_years AS SIGNED) AS n_years,
            CAST(first_year AS SIGNED) AS first_year,
            CAST(last_year AS SIGNED) AS last_year,
            mean_days,
            mean_nights,
            main_age,
            main_age_pct,
            male_pct,
            female_pct,
            age_u29_pct,
            age_3049_pct,
            age_50p_pct,
            main_purpose,
            main_purpose_pct,
            purpose_sightseeing_pct,
            purpose_business_pct,
            purpose_vfr_pct,
            purpose_meeting_pct,
            purpose_study_pct,
            purpose_medical_pct,
            top_transports,
            top_activities,
            CAST(cluster_id AS SIGNED) AS cluster_id,
            cluster_name,
            CAST(in_cluster AS SIGNED) AS in_cluster,
            ff_visitors_sum,
            ff_visitors_mean,
            CAST(ff_years AS SIGNED) AS ff_years
        ";

        #region 客群分析儀表板（全量）

        public VisitorAnalysisDashboardResponse GetDashboard()
        {
            using (var conn = Open())
            {
                conn.Open();

                List<VisitorAnalysisAttraction> attractions = conn.Query<VisitorAnalysisAttraction>(
                    @"SELECT CAST(uid AS CHAR) AS uid, name, city
                        FROM attraction
                        ORDER BY city, name;"
                ).ToList();

                List<DomesticVisitorAnalysis> domesticRows = conn.Query<DomesticVisitorAnalysis>(
                    $"SELECT {DomesticColumns} FROM domestic_analysis;"
                ).ToList();

                List<InboundVisitorAnalysis> inboundRows = conn.Query<InboundVisitorAnalysis>(
                    $"SELECT {InboundColumns} FROM inbound_analysis;"
                ).ToList();

                Dictionary<string, DomesticVisitorAnalysis> domesticMap =
                    domesticRows.ToDictionary(row => row.attraction_uid);
                Dictionary<string, InboundVisitorAnalysis> inboundMap =
                    inboundRows.ToDictionary(row => row.attraction_uid);

                foreach (VisitorAnalysisAttraction attraction in attractions)
                {
                    if (domesticMap.TryGetValue(attraction.uid, out DomesticVisitorAnalysis domestic))
                    {
                        attraction.domestic = domestic;
                    }

                    if (inboundMap.TryGetValue(attraction.uid, out InboundVisitorAnalysis inbound))
                    {
                        attraction.inbound = inbound;
                    }
                }

                return new VisitorAnalysisDashboardResponse
                {
                    attraction_count = attractions.Count,
                    domestic_count = domesticRows.Count,
                    inbound_count = inboundRows.Count,
                    attractions = attractions
                };
            }
        }

        #endregion
    }
}
