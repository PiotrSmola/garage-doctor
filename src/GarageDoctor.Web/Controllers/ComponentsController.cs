using GarageDoctor.Infrastructure.Queries;
using GarageDoctor.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GarageDoctor.Web.Controllers;

[Route("components")]
public sealed class ComponentsController : Controller
{
    private readonly IComponentQueries _components;

    public ComponentsController(IComponentQueries components)
    {
        ArgumentNullException.ThrowIfNull(components);
        _components = components;
    }

    [HttpGet("", Name = "ComponentIndex")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var groups = await _components.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var total = groups.Sum(group => (long)group.ComplaintCount);
        var leader = groups.Count == 0 ? 0 : groups.Max(group => group.ComplaintCount);

        return View(new ComponentsIndexViewModel
        {
            Groups = groups
                .Select(group => new ComponentGroupRow(
                    group.Group,
                    group.Slug,
                    group.ComplaintCount,
                    total,
                    leader))
                .ToList(),
            TotalComplaints = total
        });
    }

    [HttpGet("{slug}", Name = "ComponentDetail")]
    public async Task<IActionResult> Details(string slug, CancellationToken cancellationToken)
    {
        var normalized = slug?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(normalized))
        {
            return NotFound();
        }

        var detail = await _components.GetBySlugAsync(normalized, cancellationToken).ConfigureAwait(false);

        if (detail is null)
        {
            return NotFound();
        }

        return View(ComponentDetailViewModel.From(detail));
    }
}
