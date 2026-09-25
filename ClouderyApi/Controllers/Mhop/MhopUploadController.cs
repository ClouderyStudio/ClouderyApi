using ClouderyApi.Services.Mhop;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Controllers.Mhop;

/// <summary>文件上传：头像、帖子/回复图片（对应 Python 后端 routers/upload.py）。</summary>
[ApiController]
[Route("mhop/upload")]
public class MhopUploadController : MhopControllerBase
{
    private readonly MhopUploadService _uploads;
    private readonly MhopCurrentUserAccessor _current;

    public MhopUploadController(MhopUploadService uploads, MhopCurrentUserAccessor current)
    {
        _uploads = uploads;
        _current = current;
    }

    [HttpPost("avatar")]
    public async Task<IActionResult> UploadAvatar(IFormFile? file)
    {
        await _current.RequireAsync();
        if (file is null || file.Length == 0) throw new MhopApiException(400, "请选择要上传的图片");
        var url = await _uploads.SaveAsync(file, "avatars", HttpContext.RequestAborted);
        return MhopOk(new { url });
    }

    [HttpPost("image")]
    public async Task<IActionResult> UploadImage(IFormFile? file)
    {
        await _current.RequireAsync();
        if (file is null || file.Length == 0) throw new MhopApiException(400, "请选择要上传的图片");
        var url = await _uploads.SaveAsync(file, "posts", HttpContext.RequestAborted);
        return MhopOk(new { url });
    }
}
