using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MickMouse.Web.Data;
using MickMouse.Web.Infrastructure;
using MickMouse.Web.Services;

namespace MickMouse.Web.Controllers;

public sealed class MediaController(AppDbContext db, IMediaStore media, TimeProvider clock) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Play(string? key, CancellationToken ct)
    {
        if (media is not LocalMediaStore local || string.IsNullOrWhiteSpace(key) || key.Length > 140) return NotFound();
        var episodes = await db.Episodes.AsNoTracking().Include(x => x.Podcast)
            .Where(x => x.MediaKey == key && !x.IsDeleted).ToListAsync(ct);
        if (!episodes.Any(x => EpisodeAccess.CanView(User, x, clock.GetUtcNow().UtcDateTime))) return NotFound();
        try
        {
            var path = local.GetLocalPath(key);
            if (!System.IO.File.Exists(path)) return NotFound();
            return PhysicalFile(path, episodes[0].MediaContentType ?? "application/octet-stream", enableRangeProcessing: true);
        }
        catch (ArgumentException) { return BadRequest(); }
    }
}
