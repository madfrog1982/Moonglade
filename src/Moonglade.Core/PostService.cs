﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Edi.Practice.RequestResponseModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moonglade.Auditing;
using Moonglade.Core.Caching;
using Moonglade.Data.Entities;
using Moonglade.Data.Infrastructure;
using Moonglade.Data.Spec;
using Moonglade.DateTimeOps;
using Moonglade.Model;
using Moonglade.Model.Settings;

namespace Moonglade.Core
{
    public class PostService : BlogService
    {
        private readonly IDateTimeResolver _dateTimeResolver;
        private readonly IBlogAudit _blogAudit;
        private readonly IBlogCache _cache;

        #region Repository Objects

        private readonly IRepository<PostEntity> _postRepository;
        private readonly IRepository<PostExtensionEntity> _postExtensionRepository;
        private readonly IRepository<TagEntity> _tagRepository;
        private readonly IRepository<PostTagEntity> _postTagRepository;
        private readonly IRepository<CategoryEntity> _categoryRepository;
        private readonly IRepository<PostCategoryEntity> _postCategoryRepository;

        #endregion

        public PostService(ILogger<PostService> logger,
            IOptions<AppSettings> settings,
            IRepository<PostEntity> postRepository,
            IRepository<PostExtensionEntity> postExtensionRepository,
            IRepository<TagEntity> tagRepository,
            IRepository<PostTagEntity> postTagRepository,
            IRepository<CategoryEntity> categoryRepository,
            IRepository<PostCategoryEntity> postCategoryRepository,
            IDateTimeResolver dateTimeResolver,
            IBlogAudit blogAudit,
            IBlogCache cache) : base(logger, settings)
        {
            _postRepository = postRepository;
            _postExtensionRepository = postExtensionRepository;
            _tagRepository = tagRepository;
            _postTagRepository = postTagRepository;
            _categoryRepository = categoryRepository;
            _postCategoryRepository = postCategoryRepository;
            _dateTimeResolver = dateTimeResolver;
            _blogAudit = blogAudit;
            _cache = cache;
        }

        public Response<int> CountVisiblePosts()
        {
            return TryExecute(() =>
            {
                var count = _postRepository.Count(p => p.IsPublished && !p.IsDeleted);
                return new SuccessResponse<int>(count);
            });
        }

        public Response<int> CountByCategoryId(Guid catId)
        {
            return TryExecute(() =>
            {
                var count = _postCategoryRepository.Count(c => c.CategoryId == catId
                                                               && c.Post.IsPublished
                                                               && !c.Post.IsDeleted);

                return new SuccessResponse<int>(count);
            });
        }

        public Task<Response> UpdateStatisticAsync(Guid postId, int likes = 0)
        {
            return TryExecuteAsync(async () =>
            {
                var pp = await _postExtensionRepository.GetAsync(postId);
                if (pp == null) return new FailedResponse((int)FaultCode.PostNotFound);

                if (likes > 0)
                {
                    pp.Likes += likes;
                }
                else
                {
                    pp.Hits += 1;
                }

                await _postExtensionRepository.UpdateAsync(pp);
                return new SuccessResponse();
            }, keyParameter: postId);
        }

        public Task<Response<Post>> GetAsync(Guid id)
        {
            return TryExecuteAsync<Post>(async () =>
            {
                var spec = new PostSpec(id);
                var post = await _postRepository.SelectFirstOrDefaultAsync(spec, p => new Post
                {
                    Id = p.Id,
                    Title = p.Title,
                    Slug = p.Slug,
                    RawPostContent = p.PostContent,
                    ContentAbstract = p.ContentAbstract,
                    CommentEnabled = p.CommentEnabled,
                    CreateOnUtc = p.CreateOnUtc,
                    PubDateUtc = p.PubDateUtc,
                    IsPublished = p.IsPublished,
                    ExposedToSiteMap = p.ExposedToSiteMap,
                    FeedIncluded = p.IsFeedIncluded,
                    ContentLanguageCode = p.ContentLanguageCode,
                    Tags = p.PostTag.Select(pt => new Tag
                    {
                        Id = pt.TagId,
                        NormalizedName = pt.Tag.NormalizedName,
                        DisplayName = pt.Tag.DisplayName
                    }).ToArray(),
                    Categories = p.PostCategory.Select(pc => new Category
                    {
                        Id = pc.CategoryId,
                        DisplayName = pc.Category.DisplayName,
                        RouteName = pc.Category.RouteName,
                        Note = pc.Category.Note
                    }).ToArray()
                });
                return new SuccessResponse<Post>(post);
            });
        }

        public Task<Response<PostSlug>> GetDraftPreviewAsync(Guid postId)
        {
            return TryExecuteAsync<PostSlug>(async () =>
            {
                var spec = new PostSpec(postId);
                var postSlugModel = await _postRepository.SelectFirstOrDefaultAsync(spec, post => new PostSlug
                {
                    Title = post.Title,
                    ContentAbstract = post.ContentAbstract,
                    PubDateUtc = DateTime.UtcNow,

                    Categories = post.PostCategory.Select(pc => pc.Category).Select(p => new Category
                    {
                        DisplayName = p.DisplayName,
                        RouteName = p.RouteName
                    }).ToArray(),

                    RawPostContent = post.PostContent,

                    Tags = post.PostTag.Select(pt => pt.Tag)
                        .Select(p => new Tag
                        {
                            NormalizedName = p.NormalizedName,
                            DisplayName = p.DisplayName
                        }).ToArray(),
                    Id = post.Id,
                    ExposedToSiteMap = post.ExposedToSiteMap,
                    LastModifyOnUtc = post.LastModifiedUtc,
                    ContentLanguageCode = post.ContentLanguageCode
                });

                if (null != postSlugModel)
                {
                    postSlugModel.RawPostContent = Utils.AddLazyLoadToImgTag(postSlugModel.RawPostContent);
                }

                return new SuccessResponse<PostSlug>(postSlugModel);
            });
        }

        public Task<Response<string>> GetRawContentAsync(PostSlugInfo slugInfo)
        {
            return TryExecuteAsync<string>(async () =>
            {
                var date = new DateTime(slugInfo.Year, slugInfo.Month, slugInfo.Day);
                var spec = new PostSpec(date, slugInfo.Slug);

                var model = await _postRepository.SelectFirstOrDefaultAsync(spec,
                    post => post.PostContent);
                return new SuccessResponse<string>(model);
            });
        }

        public Task<Response<PostSlugSegment>> GetSegmentAsync(PostSlugInfo slugInfo)
        {
            return TryExecuteAsync<PostSlugSegment>(async () =>
            {
                var date = new DateTime(slugInfo.Year, slugInfo.Month, slugInfo.Day);
                var spec = new PostSpec(date, slugInfo.Slug);

                var model = await _postRepository.SelectFirstOrDefaultAsync(spec, post => new PostSlugSegment
                {
                    Title = post.Title,
                    PubDateUtc = post.PubDateUtc.GetValueOrDefault(),
                    LastModifyOnUtc = post.LastModifiedUtc,

                    Categories = post.PostCategory
                                     .Select(pc => pc.Category.DisplayName)
                                     .ToArray(),

                    Tags = post.PostTag
                               .Select(pt => pt.Tag.DisplayName)
                               .ToArray()
                });

                return new SuccessResponse<PostSlugSegment>(model);
            });
        }

        public Task<Response<PostSlug>> GetAsync(PostSlugInfo slugInfo)
        {
            return TryExecuteAsync<PostSlug>(async () =>
            {
                var date = new DateTime(slugInfo.Year, slugInfo.Month, slugInfo.Day);
                var spec = new PostSpec(date, slugInfo.Slug);

                var pid = await _postRepository.SelectFirstOrDefaultAsync(spec, p => p.Id);
                if (pid != Guid.Empty)
                {
                    var psm = await _cache.GetOrCreateAsync(CacheDivision.Post, $"{pid}", async entry =>
                    {
                        entry.SlidingExpiration = TimeSpan.FromMinutes(AppSettings.CacheSlidingExpirationMinutes["Post"]);

                        var postSlugModel = await _postRepository.SelectFirstOrDefaultAsync(spec, post => new PostSlug
                        {
                            Title = post.Title,
                            ContentAbstract = post.ContentAbstract,
                            PubDateUtc = post.PubDateUtc.GetValueOrDefault(),

                            Categories = post.PostCategory.Select(pc => pc.Category).Select(p => new Category
                            {
                                DisplayName = p.DisplayName,
                                RouteName = p.RouteName
                            }).ToArray(),

                            RawPostContent = post.PostContent,
                            Hits = post.PostExtension.Hits,
                            Likes = post.PostExtension.Likes,

                            Tags = post.PostTag.Select(pt => pt.Tag)
                                .Select(p => new Tag
                                {
                                    NormalizedName = p.NormalizedName,
                                    DisplayName = p.DisplayName
                                }).ToArray(),
                            Id = post.Id,
                            CommentEnabled = post.CommentEnabled,
                            ExposedToSiteMap = post.ExposedToSiteMap,
                            LastModifyOnUtc = post.LastModifiedUtc,
                            ContentLanguageCode = post.ContentLanguageCode,
                            CommentCount = post.Comment.Count(c => c.IsApproved)
                        });

                        if (null != postSlugModel)
                        {
                            postSlugModel.RawPostContent = Utils.AddLazyLoadToImgTag(postSlugModel.RawPostContent);
                        }

                        return postSlugModel;
                    });

                    return new SuccessResponse<PostSlug>(psm);
                }

                return new SuccessResponse<PostSlug>(null);
            });
        }

        public Task<IReadOnlyList<PostSegment>> ListSegmentAsync(PostPublishStatus postPublishStatus)
        {
            var spec = new PostSpec(postPublishStatus);
            return _postRepository.SelectAsync(spec, p => new PostSegment
            {
                Id = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                PubDateUtc = p.PubDateUtc,
                IsPublished = p.IsPublished,
                IsDeleted = p.IsDeleted,
                CreateOnUtc = p.CreateOnUtc,
                Hits = p.PostExtension.Hits
            });
        }

        public Task<IReadOnlyList<PostSegment>> GetInsightsAsync(PostInsightsType insightsType)
        {
            var spec = new PostInsightsSpec(insightsType, 10);
            return _postRepository.SelectAsync(spec, p => new PostSegment
            {
                Id = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                PubDateUtc = p.PubDateUtc,
                IsPublished = p.IsPublished,
                IsDeleted = p.IsDeleted,
                CreateOnUtc = p.CreateOnUtc,
                Hits = p.PostExtension.Hits
            });
        }

        public Task<IReadOnlyList<PostListEntry>> GetPagedPostsAsync(int pageSize, int pageIndex, Guid? categoryId = null)
        {
            if (pageSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize),
                    $"{nameof(pageSize)} can not be less than 1, current value: {pageSize}.");
            }
            if (pageIndex < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndex),
                    $"{nameof(pageIndex)} can not be less than 1, current value: {pageIndex}.");
            }

            var spec = new PostPagingSpec(pageSize, pageIndex, categoryId);
            return _postRepository.SelectAsync(spec, p => new PostListEntry
            {
                Title = p.Title,
                Slug = p.Slug,
                ContentAbstract = p.ContentAbstract,
                PubDateUtc = p.PubDateUtc.GetValueOrDefault(),
                LangCode = p.ContentLanguageCode,
                Tags = p.PostTag.Select(pt => new Tag
                {
                    NormalizedName = pt.Tag.NormalizedName,
                    DisplayName = pt.Tag.DisplayName
                })
            });
        }

        public Task<Response<IReadOnlyList<PostListEntry>>> GetByTagAsync(int tagId)
        {
            return TryExecuteAsync<IReadOnlyList<PostListEntry>>(async () =>
            {
                if (tagId == 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(tagId));
                }

                var posts = await _postTagRepository.SelectAsync(new PostTagSpec(tagId),
                    p => new PostListEntry
                    {
                        Title = p.Post.Title,
                        Slug = p.Post.Slug,
                        ContentAbstract = p.Post.ContentAbstract,
                        PubDateUtc = p.Post.PubDateUtc.GetValueOrDefault(),
                        LangCode = p.Post.ContentLanguageCode
                    });

                return new SuccessResponse<IReadOnlyList<PostListEntry>>(posts);
            });
        }

        public async Task<Response<PostEntity>> CreateAsync(CreatePostRequest request)
        {
            return await TryExecuteAsync<PostEntity>(async () =>
            {
                var postModel = new PostEntity
                {
                    CommentEnabled = request.EnableComment,
                    Id = Guid.NewGuid(),
                    PostContent = request.EditorContent,
                    ContentAbstract = Utils.GetPostAbstract(
                                            request.EditorContent,
                                            AppSettings.PostAbstractWords,
                                            AppSettings.Editor == EditorChoice.Markdown),
                    CreateOnUtc = DateTime.UtcNow,
                    Slug = request.Slug.ToLower().Trim(),
                    Title = request.Title.Trim(),
                    ContentLanguageCode = request.ContentLanguageCode,
                    ExposedToSiteMap = request.ExposedToSiteMap,
                    IsFeedIncluded = request.IsFeedIncluded,
                    PubDateUtc = request.IsPublished ? DateTime.UtcNow : (DateTime?)null,
                    IsDeleted = false,
                    IsPublished = request.IsPublished,
                    PostExtension = new PostExtensionEntity
                    {
                        Hits = 0,
                        Likes = 0
                    }
                };

                // check if exist same slug under the same day
                // linq to sql fix:
                // cannot write "p.PubDateUtc.GetValueOrDefault().Date == DateTime.UtcNow.Date"
                // it will not blow up, but can result in select ENTIRE posts and evaluated in memory!!!
                // - The LINQ expression 'where (Convert([p]?.PubDateUtc?.GetValueOrDefault(), DateTime).Date == DateTime.UtcNow.Date)' could not be translated and will be evaluated locally
                // Why EF Core this diao yang?
                if (_postRepository.Any(p =>
                    p.Slug == postModel.Slug &&
                    p.PubDateUtc != null &&
                    p.PubDateUtc.Value.Year == DateTime.UtcNow.Date.Year &&
                    p.PubDateUtc.Value.Month == DateTime.UtcNow.Date.Month &&
                    p.PubDateUtc.Value.Day == DateTime.UtcNow.Date.Day))
                {
                    var uid = Guid.NewGuid();
                    postModel.Slug += $"-{uid.ToString().ToLower().Substring(0, 8)}";
                    Logger.LogInformation($"Found conflict for post slug, generated new slug: {postModel.Slug}");
                }

                // add categories
                if (null != request.CategoryIds && request.CategoryIds.Length > 0)
                {
                    foreach (var cid in request.CategoryIds)
                    {
                        if (_categoryRepository.Any(c => c.Id == cid))
                        {
                            postModel.PostCategory.Add(new PostCategoryEntity
                            {
                                CategoryId = cid,
                                PostId = postModel.Id
                            });
                        }
                    }
                }

                // add tags
                if (null != request.Tags && request.Tags.Length > 0)
                {
                    foreach (var item in request.Tags)
                    {
                        if (!Utils.ValidateTagName(item))
                        {
                            continue;
                        }

                        var tag = await _tagRepository.GetAsync(q => q.DisplayName == item);
                        if (null == tag)
                        {
                            var newTag = new TagEntity
                            {
                                DisplayName = item,
                                NormalizedName = Utils.NormalizeTagName(item)
                            };

                            tag = await _tagRepository.AddAsync(newTag);
                            await _blogAudit.AddAuditEntry(EventType.Content, AuditEventId.TagCreated,
                                $"Tag '{tag.NormalizedName}' created.");
                        }

                        postModel.PostTag.Add(new PostTagEntity
                        {
                            TagId = tag.Id,
                            PostId = postModel.Id
                        });
                    }
                }

                await _postRepository.AddAsync(postModel);
                await _blogAudit.AddAuditEntry(EventType.Content, AuditEventId.PostCreated, $"Post created, id: {postModel.Id}");

                return new SuccessResponse<PostEntity>(postModel);
            });
        }

        public async Task<Response<PostEntity>> UpdateAsync(EditPostRequest request)
        {
            return await TryExecuteAsync<PostEntity>(async () =>
            {
                var postModel = await _postRepository.GetAsync(request.Id);
                if (null == postModel)
                {
                    return new FailedResponse<PostEntity>((int)FaultCode.PostNotFound);
                }

                postModel.CommentEnabled = request.EnableComment;
                postModel.PostContent = request.EditorContent;
                postModel.ContentAbstract = Utils.GetPostAbstract(
                                            request.EditorContent,
                                            AppSettings.PostAbstractWords,
                                            AppSettings.Editor == EditorChoice.Markdown);

                // Address #221: Do not allow published posts back to draft status
                // postModel.IsPublished = request.IsPublished;
                // Edit draft -> save and publish, ignore false case because #221
                bool isNewPublish = false;
                if (request.IsPublished && !postModel.IsPublished)
                {
                    postModel.IsPublished = true;
                    postModel.PubDateUtc = DateTime.UtcNow;

                    isNewPublish = true;
                }

                // #325: Allow changing publish date for published posts
                if (request.PublishDate != null && postModel.PubDateUtc.HasValue)
                {
                    var tod = postModel.PubDateUtc.Value.TimeOfDay;
                    var adjustedDate = _dateTimeResolver.ToUtc(request.PublishDate.Value);
                    postModel.PubDateUtc = adjustedDate.AddTicks(tod.Ticks);
                }

                postModel.Slug = request.Slug;
                postModel.Title = request.Title;
                postModel.ExposedToSiteMap = request.ExposedToSiteMap;
                postModel.LastModifiedUtc = DateTime.UtcNow;
                postModel.IsFeedIncluded = request.IsFeedIncluded;
                postModel.ContentLanguageCode = request.ContentLanguageCode;

                // 1. Add new tags to tag lib
                foreach (var item in request.Tags.Where(item => !_tagRepository.Any(p => p.DisplayName == item)))
                {
                    await _tagRepository.AddAsync(new TagEntity
                    {
                        DisplayName = item,
                        NormalizedName = Utils.NormalizeTagName(item)
                    });

                    await _blogAudit.AddAuditEntry(EventType.Content, AuditEventId.TagCreated,
                        $"Tag '{item}' created.");
                }

                // 2. update tags
                postModel.PostTag.Clear();
                if (request.Tags.Any())
                {
                    foreach (var tagName in request.Tags)
                    {
                        if (!Utils.ValidateTagName(tagName))
                        {
                            continue;
                        }

                        var tag = await _tagRepository.GetAsync(t => t.DisplayName == tagName);
                        if (tag != null) postModel.PostTag.Add(new PostTagEntity
                        {
                            PostId = postModel.Id,
                            TagId = tag.Id
                        });
                    }
                }

                // 3. update categories
                postModel.PostCategory.Clear();
                if (null != request.CategoryIds && request.CategoryIds.Length > 0)
                {
                    foreach (var cid in request.CategoryIds)
                    {
                        if (_categoryRepository.Any(c => c.Id == cid))
                        {
                            postModel.PostCategory.Add(new PostCategoryEntity
                            {
                                PostId = postModel.Id,
                                CategoryId = cid
                            });
                        }
                    }
                }

                await _postRepository.UpdateAsync(postModel);

                await _blogAudit.AddAuditEntry(
                    EventType.Content,
                    isNewPublish ? AuditEventId.PostPublished : AuditEventId.PostUpdated,
                    $"Post updated, id: {postModel.Id}");

                _cache.Remove(CacheDivision.Post, request.Id.ToString());
                return new SuccessResponse<PostEntity>(postModel);
            });
        }

        public Task<Response> RestoreDeletedAsync(Guid postId)
        {
            return TryExecuteAsync(async () =>
            {
                var pp = await _postRepository.GetAsync(postId);
                if (null == pp) return new FailedResponse((int)FaultCode.PostNotFound);

                pp.IsDeleted = false;
                await _postRepository.UpdateAsync(pp);
                await _blogAudit.AddAuditEntry(EventType.Content, AuditEventId.PostRestored, $"Post restored, id: {postId}");

                _cache.Remove(CacheDivision.Post, postId.ToString());
                return new SuccessResponse();
            }, keyParameter: postId);
        }

        public Task<Response> DeleteAsync(Guid postId, bool isRecycle = false)
        {
            return TryExecuteAsync(async () =>
            {
                var post = await _postRepository.GetAsync(postId);
                if (null == post) return new FailedResponse((int)FaultCode.PostNotFound);

                if (isRecycle)
                {
                    post.IsDeleted = true;
                    await _postRepository.UpdateAsync(post);
                    await _blogAudit.AddAuditEntry(EventType.Content, AuditEventId.PostRecycled, $"Post '{postId}' moved to Recycle Bin.");
                }
                else
                {
                    await _postRepository.DeleteAsync(post);
                    await _blogAudit.AddAuditEntry(EventType.Content, AuditEventId.PostDeleted, $"Post '{postId}' deleted from Recycle Bin.");
                }

                _cache.Remove(CacheDivision.Post, postId.ToString());
                return new SuccessResponse();
            }, keyParameter: postId);
        }

        public Task<Response> DeleteRecycledAsync()
        {
            return TryExecuteAsync(async () =>
            {
                var spec = new PostSpec(true);
                var posts = await _postRepository.GetAsync(spec);
                await _postRepository.DeleteAsync(posts);
                await _blogAudit.AddAuditEntry(EventType.Content, AuditEventId.EmptyRecycleBin, "Emptied Recycle Bin.");

                foreach (var guid in posts.Select(p => p.Id))
                {
                    _cache.Remove(CacheDivision.Post, guid.ToString());
                }
                return new SuccessResponse();
            });
        }
    }
}