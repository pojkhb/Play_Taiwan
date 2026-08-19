using System.Collections.Generic;
using System.Linq;
using backend.Dao;
using backend.ViewModels;

namespace backend.Services
{
    /// <summary>明信片主檔 (md_postcard) 服務層。</summary>
    public class PostcardCatalogService
    {
        private readonly PostcardCatalogDao _dao;

        public PostcardCatalogService(PostcardCatalogDao dao)
        {
            _dao = dao;
        }

        public List<PostcardCatalogResponse> GetAll(string category = null)
            => _dao.GetAllAsync(category).Result.Select(ToResponse).ToList();

        public PostcardCatalogResponse GetById(string id)
        {
            var entity = _dao.GetByIdAsync(id).Result;
            return entity == null ? null : ToResponse(entity);
        }

        public List<PostcardCatalogResponse> GetByStoryId(string storyId)
            => _dao.GetByStoryIdAsync(storyId).Result.Select(ToResponse).ToList();

        public void Create(PostcardCatalogRequest request)
        {
            _dao.CreateAsync(new Models.PostcardCatalog
            {
                PostcardId = request.PostcardId,
                StoryId = request.StoryId,
                PostcardName = request.PostcardName,
                Summary = request.Summary,
                ImageUrl = request.ImageUrl,
                IsNightEditionDefault = request.IsNightEditionDefault,
                Category = request.Category,
                SortOrder = request.SortOrder,
                IsActive = request.IsActive
            }).Wait();
        }

        public bool Update(string id, PostcardCatalogRequest request)
        {
            return _dao.UpdateAsync(new Models.PostcardCatalog
            {
                PostcardId = id,
                StoryId = request.StoryId,
                PostcardName = request.PostcardName,
                Summary = request.Summary,
                ImageUrl = request.ImageUrl,
                IsNightEditionDefault = request.IsNightEditionDefault,
                Category = request.Category,
                SortOrder = request.SortOrder,
                IsActive = request.IsActive
            }).Result;
        }

        public bool Delete(string id) => _dao.DeleteAsync(id).Result;

        private static PostcardCatalogResponse ToResponse(Models.PostcardCatalog e) => new PostcardCatalogResponse
        {
            PostcardId = e.PostcardId,
            StoryId = e.StoryId,
            PostcardName = e.PostcardName,
            Summary = e.Summary,
            ImageUrl = e.ImageUrl,
            IsNightEditionDefault = e.IsNightEditionDefault,
            Category = e.Category,
            SortOrder = e.SortOrder,
            IsActive = e.IsActive,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt
        };
    }
}