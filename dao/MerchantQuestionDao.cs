using System.Collections.Generic;
using System.Linq;
using Dapper;
using backend.Models;
using backend.utils;
using backend.ViewModels;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace backend.dao
{
    public class MerchantQuestionDao
    {
        private readonly AppSettings _appSettings;

        public MerchantQuestionDao(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }

        public int Create(QuestionCreateRequest req)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                int questionId = connection.ExecuteScalar<int>(@"
                        INSERT INTO store_question (store_id, question_describe)
                        VALUES (@store_id, @question_describe);
                        SELECT LAST_INSERT_ID();
                    ", new { req.store_id, req.question_describe }, transaction);

                InsertOptions(connection, transaction, questionId, req.options);

                transaction.Commit();
                return questionId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public List<QuestionResponse> GetByStore(int storeId)
        {
            const string sql = @"
                SELECT question_id, store_id, question_describe FROM store_question WHERE store_id = @storeId;
                SELECT o.option_id, o.question_id, o.option_context, o.option_url, o.is_correct, o.option_key
                FROM question_option o
                JOIN store_question q ON q.question_id = o.question_id
                WHERE q.store_id = @storeId;
            ";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            using var multi = connection.QueryMultiple(sql, new { storeId });

            var questions = multi.Read<QuestionResponse>().ToList();
            var options = multi.Read<QuestionOptionWithQuestionId>().ToList();

            foreach (var question in questions)
            {
                question.options = options
                    .Where(o => o.question_id == question.question_id)
                    .Select(o => new QuestionOptionResponse
                    {
                        option_id = o.option_id,
                        option_context = o.option_context,
                        option_url = o.option_url,
                        is_correct = o.is_correct,
                        option_key = o.option_key
                    }).ToList();
            }
            return questions;
        }

        public QuestionResponse GetById(int questionId)
        {
            const string sql = @"
                SELECT question_id, store_id, question_describe FROM store_question WHERE question_id = @questionId LIMIT 1;
                SELECT option_id, option_context, option_url, is_correct, option_key
                FROM question_option WHERE question_id = @questionId;
            ";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            using var multi = connection.QueryMultiple(sql, new { questionId });

            var question = multi.Read<QuestionResponse>().FirstOrDefault();
            if (question == null) return null;

            question.options = multi.Read<QuestionOptionResponse>().ToList();
            return question;
        }

        /// <summary>整包覆蓋選項：刪除舊選項後重新插入，跟修改題目文字包在同一個 Transaction。</summary>
        public bool Update(int questionId, QuestionUpdateRequest req)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                int affected = connection.Execute(@"
                        UPDATE store_question SET question_describe = @question_describe WHERE question_id = @questionId;
                    ", new { questionId, req.question_describe }, transaction);

                connection.Execute("DELETE FROM question_option WHERE question_id = @questionId;", new { questionId }, transaction);
                InsertOptions(connection, transaction, questionId, req.options);

                transaction.Commit();
                return affected > 0;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>查詢是否有 task 引用這一題，回傳所有引用的 task_id。</summary>
        public List<int> GetReferencingTaskIds(int questionId)
        {
            const string sql = "SELECT task_id FROM task WHERE question_id = @questionId;";
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            return connection.Query<int>(sql, new { questionId }).ToList();
        }

        /// <summary>刪除題目：呼叫端要先確認沒有 task 引用（見 GetReferencingTaskIds）。</summary>
        public bool Delete(int questionId)
        {
            using var connection = new MySqlConnection(_appSettings.mydb);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                connection.Execute("DELETE FROM question_option WHERE question_id = @questionId;", new { questionId }, transaction);
                int affected = connection.Execute("DELETE FROM store_question WHERE question_id = @questionId;", new { questionId }, transaction);

                transaction.Commit();
                return affected > 0;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static void InsertOptions(MySqlConnection connection, MySqlTransaction transaction, int questionId, List<QuestionOptionInput> options)
        {
            const string sql = @"
                INSERT INTO question_option (question_id, option_context, option_url, is_correct, option_key)
                VALUES (@questionId, @option_context, @option_url, @is_correct, @option_key);
            ";
            foreach (var option in options)
            {
                connection.Execute(sql, new
                {
                    questionId,
                    option.option_context,
                    option.option_url,
                    is_correct = option.is_correct,
                    option.option_key
                }, transaction);
            }
        }

        private class QuestionOptionWithQuestionId
        {
            public int option_id { get; set; }
            public int question_id { get; set; }
            public string option_context { get; set; }
            public string option_url { get; set; }
            public bool is_correct { get; set; }
            public string option_key { get; set; }
        }
    }
}
