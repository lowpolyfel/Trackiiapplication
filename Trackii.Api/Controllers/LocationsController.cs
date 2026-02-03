using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Trackii.Api.Data;

namespace Trackii.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class LocationsController : ControllerBase
{
    private readonly TrackiiDbContext _dbContext;

    public LocationsController(TrackiiDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetActiveLocations(CancellationToken cancellationToken)
    {
        var locations = await _dbContext.Locations
            .Where(location => location.Active)
            .OrderBy(location => location.Name)
            .Select(location => new
            {
                location.Id,
                location.Name
            })
            .ToListAsync(cancellationToken);

        return Ok(locations);
    }
}
