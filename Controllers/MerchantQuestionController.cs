using System.Collections.Generic;
using backend.Services;
using backend.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>商家題庫維護。</summary>
    [ApiController]
    [Route("api/merchant/questions")]
    public class MerchantQuestionController : ControllerBase
    {
        private readonly MerchantQuestionService _service;

        public MerchantQuestionController(MerchantQuestionService service)
        {
            _service = service;
        }

        /// <summary>新增題目。</summary>
        /// <remarks>含選項，選項中須至少一個 is_correct=1。</remarks>
        [HttpPost]
        [Route("")]
        public IActionResult Create([FromBody] QuestionCreateRequest req)
        {
            int questionId = _service.Create(req);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "題目新增成功", Result = new { question_id = questionId } });
        }

        /// <summary>查詢商家題庫列表。</summary>
        [HttpGet]
        [Route("")]
        public IActionResult GetByStore([FromQuery] int storeId)
        {
            List<QuestionResponse> result = _service.GetByStore(storeId);
            return Ok(new ResultViewModel<List<QuestionResponse>> { isSuccess = true, message = "查詢成功", Result = result });
        }

        /// <summary>修改題目與選項。</summary>
        /// <remarks>整包覆蓋選項：舊選項會先被刪除，再依 request 內容重新建立。</remarks>
        [HttpPut]
        [Route("{questionId:int}")]
        public IActionResult Update(int questionId, [FromBody] QuestionUpdateRequest req)
        {
            _service.Update(questionId, req);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "題目修改成功", Result = null });
        }

        /// <summary>刪除題目。</summary>
        /// <remarks>刪除前檢查是否有任務引用這一題；若有，回傳 409 並附上引用的 task_id 清單。</remarks>
        [HttpDelete]
        [Route("{questionId:int}")]
        public IActionResult Delete(int questionId)
        {
            _service.Delete(questionId);
            return Ok(new ResultViewModel<object> { isSuccess = true, message = "題目已刪除", Result = null });
        }
    }
}
