using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend.dao;
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

        public async Task<List<PostcardCatalogResponse>> GetAllAsync(string category = null)
        {
            var entities = await _dao.GetAllAsync(category);
            return entities.Select(ToResponse).ToList();
        }

        public async Task<PostcardCatalogResponse> GetByIdAsync(string id)
        {
            var entity = await _dao.GetByIdAsync(id);
            return entity == null ? null : ToResponse(entity);
        }

        public async Task<List<PostcardCatalogResponse>> GetByStoryIdAsync(string storyId)
        {
            var entities = await _dao.GetByStoryIdAsync(storyId);
            return entities.Select(ToResponse).ToList();
        }

        public async Task CreateAsync(PostcardCatalogRequest request)
        {
            await _dao.CreateAsync(new Models.PostcardCatalog
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
            });
        }

        public async Task<bool> UpdateAsync(string id, PostcardCatalogRequest request)
        {
            return await _dao.UpdateAsync(new Models.PostcardCatalog
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
            });
        }

        public async Task<bool> DeleteAsync(string id)
        {
            return await _dao.DeleteAsync(id);
        }

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