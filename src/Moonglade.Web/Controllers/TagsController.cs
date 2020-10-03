﻿using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moonglade.Core;
using Moonglade.Model.Settings;

namespace Moonglade.Web.Controllers
{
    [Route("tags")]
    public class TagsController : BlogController
    {
        private readonly TagService _tagService;
        private readonly PostService _postService;

        public TagsController(
            ILogger<TagsController> logger,
            IOptions<AppSettings> settings,
            TagService tagService,
            PostService postService)
            : base(logger, settings)
        {
            _tagService = tagService;
            _postService = postService;
        }

        [Route("")]
        public async Task<IActionResult> Index()
        {
            try
            {
                var tags = await _tagService.GetTagCountListAsync();
                return View(tags);
            }
            catch (Exception e)
            {
                SetFriendlyErrorMessage();
                Logger.LogError(e, e.Message);
                return View();
            }
        }

        [Route("list/{normalizedName:regex(^(?!-)([[a-zA-Z0-9-]]+)$)}")]
        public async Task<IActionResult> List(string normalizedName)
        {
            try
            {
                var tagResponse = _tagService.Get(normalizedName);
                if (tagResponse == null) return NotFound();

                ViewBag.TitlePrefix = tagResponse.DisplayName;
                var postResponse = await _postService.GetByTagAsync(tagResponse.Id);
                if (!postResponse.IsSuccess)
                {
                    SetFriendlyErrorMessage();
                    return View();
                }

                var posts = postResponse.Item;
                return View(posts);
            }
            catch (Exception e)
            {
                Logger.LogError(e, e.Message);
                SetFriendlyErrorMessage();
                return View();
            }
        }

        [Route("get-all-tag-names")]
        public async Task<IActionResult> GetAllTagNames()
        {
            var tagNames = await _tagService.GetAllNamesAsync();
            return Json(tagNames);
        }

        [Authorize]
        [Route("manage")]
        public async Task<IActionResult> Manage()
        {
            var tags = await _tagService.GetAllAsync();
            return View("~/Views/Admin/ManageTags.cshtml", tags);
        }

        [Authorize]
        [HttpPost("update")]
        public async Task<IActionResult> Update(int tagId, string newTagName)
        {
            await _tagService.UpdateAsync(tagId, newTagName);
            return Json(new { tagId, newTagName });
        }

        [Authorize]
        [HttpPost("delete")]
        public async Task<IActionResult> Delete(int tagId)
        {
            await _tagService.DeleteAsync(tagId);
            return Json(tagId);
        }
    }
}