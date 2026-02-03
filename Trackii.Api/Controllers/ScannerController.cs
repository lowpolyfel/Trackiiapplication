using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Trackii.Api.Contracts;
using Trackii.Api.Data;
using Trackii.Api.Models;

namespace Trackii.Api.Controllers;

[ApiController]
[Route("api/scanner")]
public sealed class ScannerController : ControllerBase
{
    private readonly TrackiiDbContext _dbContext;

    public ScannerController(TrackiiDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("part/{partNumber}")]
    public async Task<IActionResult> GetPartInfo(string partNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(partNumber))
        {
            return BadRequest("Número de parte requerido.");
        }

        var normalized = partNumber.Trim();
        var product = await _dbContext.Products
            .Include(p => p.Subfamily)
            .ThenInclude(sf => sf.Family)
            .ThenInclude(f => f.Area)
            .FirstOrDefaultAsync(p => p.PartNumber == normalized && p.Active, cancellationToken);

        if (product is null || product.Subfamily is null || product.Subfamily.Family is null || product.Subfamily.Family.Area is null)
        {
            _dbContext.UnregisteredParts.Add(new UnregisteredPart
            {
                PartNumber = normalized,
                CreationDateTime = DateTime.UtcNow,
                Active = true
            });
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(new PartLookupResponse(
                false,
                "El producto no está dado de alta. Contacta a ingeniería.",
                normalized,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null));
        }

        return Ok(new PartLookupResponse(
            true,
            null,
            normalized,
            product.Id,
            product.Subfamily.Id,
            product.Subfamily.Name,
            product.Subfamily.Family.Id,
            product.Subfamily.Family.Name,
            product.Subfamily.Family.Area.Id,
            product.Subfamily.Family.Area.Name,
            product.Subfamily.ActiveRouteId));
    }

    [HttpGet("work-orders/{woNumber}/context")]
    public async Task<IActionResult> GetWorkOrderContext(string woNumber, [FromQuery] uint deviceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(woNumber))
        {
            return BadRequest("Número de orden requerido.");
        }

        var workOrder = await _dbContext.WorkOrders
            .Include(wo => wo.Product)
            .ThenInclude(p => p.Subfamily)
            .ThenInclude(sf => sf.ActiveRoute)
            .Include(wo => wo.WipItem)
            .FirstOrDefaultAsync(wo => wo.WoNumber == woNumber, cancellationToken);

        if (workOrder is null || workOrder.Product is null || workOrder.Product.Subfamily is null)
        {
            return Ok(new WorkOrderContextResponse(
                false,
                "Orden no encontrada.",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                false));
        }

        var routeId = workOrder.Product.Subfamily.ActiveRouteId;
        if (routeId is null)
        {
            return Ok(new WorkOrderContextResponse(
                true,
                "La subfamilia no tiene ruta activa.",
                workOrder.Id,
                workOrder.Status,
                workOrder.ProductId,
                workOrder.Product.PartNumber,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                false));
        }

        var device = await _dbContext.Devices
            .Include(d => d.Location)
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.Active, cancellationToken);

        if (device is null || device.Location is null)
        {
            return Ok(new WorkOrderContextResponse(
                true,
                "Dispositivo inválido.",
                workOrder.Id,
                workOrder.Status,
                workOrder.ProductId,
                workOrder.Product.PartNumber,
                routeId,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                false));
        }

        var steps = await _dbContext.RouteSteps
            .Where(step => step.RouteId == routeId.Value)
            .OrderBy(step => step.StepNumber)
            .ToListAsync(cancellationToken);

        if (steps.Count == 0)
        {
            return Ok(new WorkOrderContextResponse(
                true,
                "La ruta no tiene pasos configurados.",
                workOrder.Id,
                workOrder.Status,
                workOrder.ProductId,
                workOrder.Product.PartNumber,
                routeId,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                false));
        }

        var isFirstStep = workOrder.WipItem is null;
        uint? previousQty = null;
        uint? maxQty = null;
        RouteStep? nextStep;
        RouteStep? currentStep = null;

        if (isFirstStep)
        {
            nextStep = steps.First();
        }
        else
        {
            currentStep = steps.FirstOrDefault(step => step.Id == workOrder.WipItem!.CurrentStepId);
            if (currentStep is null)
            {
                return Ok(new WorkOrderContextResponse(
                    true,
                    "Paso actual inválido.",
                    workOrder.Id,
                    workOrder.Status,
                    workOrder.ProductId,
                    workOrder.Product.PartNumber,
                    routeId,
                    workOrder.WipItem!.CurrentStepId,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    false,
                    false));
            }

            nextStep = steps.FirstOrDefault(step => step.StepNumber == currentStep.StepNumber + 1);
            var previousExecution = await _dbContext.WipStepExecutions
                .FirstOrDefaultAsync(exec => exec.WipItemId == workOrder.WipItem!.Id && exec.RouteStepId == currentStep.Id, cancellationToken);
            if (previousExecution is not null)
            {
                previousQty = previousExecution.QtyIn;
                maxQty = previousExecution.QtyIn;
            }
        }

        if (nextStep is null)
        {
            return Ok(new WorkOrderContextResponse(
                true,
                "La orden ya está en el último paso.",
                workOrder.Id,
                workOrder.Status,
                workOrder.ProductId,
                workOrder.Product.PartNumber,
                routeId,
                currentStep?.Id,
                null,
                null,
                null,
                null,
                previousQty,
                maxQty,
                isFirstStep,
                false));
        }

        var nextLocation = await _dbContext.Locations
            .FirstOrDefaultAsync(location => location.Id == nextStep.LocationId, cancellationToken);
        var isOnRework = workOrder.WipItem is not null && workOrder.WipItem.Status == "HOLD";
        var canProceed = !isOnRework && workOrder.Status != "CANCELLED" && workOrder.Status != "FINISHED";
        return Ok(new WorkOrderContextResponse(
            true,
            canProceed ? null : isOnRework ? "El WIP está en rework." : "La orden no permite avanzar.",
            workOrder.Id,
            workOrder.Status,
            workOrder.ProductId,
            workOrder.Product.PartNumber,
            routeId,
            currentStep?.Id,
            nextStep.Id,
            nextStep.StepNumber,
            nextStep.LocationId,
            nextLocation?.Name,
            previousQty,
            maxQty,
            isFirstStep,
            canProceed));
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterScan([FromBody] RegisterScanRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Solicitud inválida.");
        }

        if (string.IsNullOrWhiteSpace(request.WorkOrderNumber) || string.IsNullOrWhiteSpace(request.PartNumber))
        {
            return BadRequest("Orden y número de parte son requeridos.");
        }

        if (request.Quantity == 0)
        {
            return BadRequest("Cantidad inválida.");
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.Active, cancellationToken);
        if (user is null)
        {
            return Unauthorized("Usuario inválido.");
        }

        var device = await _dbContext.Devices
            .Include(d => d.Location)
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId && d.Active, cancellationToken);
        if (device is null || device.UserId != user.Id)
        {
            return Unauthorized("Dispositivo inválido.");
        }

        async Task LogSkipStepAsync(uint wipItemId, uint routeStepId)
        {
            _dbContext.ScanEvents.Add(new ScanEvent
            {
                WipItemId = wipItemId,
                RouteStepId = routeStepId,
                ScanType = "ERROR",
                Ts = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var workOrder = await _dbContext.WorkOrders
            .Include(wo => wo.Product)
            .ThenInclude(p => p.Subfamily)
            .FirstOrDefaultAsync(wo => wo.WoNumber == request.WorkOrderNumber.Trim(), cancellationToken);

        if (workOrder is null)
        {
            var canCreateWorkOrder = device.LocationId == 1
                || (device.Location?.Name is not null
                    && device.Location.Name.Equals("Alloy", StringComparison.OrdinalIgnoreCase));

            if (!canCreateWorkOrder)
            {
                return BadRequest("Orden no encontrada.");
            }

            var product = await _dbContext.Products
                .Include(p => p.Subfamily)
                .FirstOrDefaultAsync(p => p.PartNumber == request.PartNumber.Trim() && p.Active, cancellationToken);
            if (product is null || product.Subfamily is null)
            {
                return BadRequest("Producto no encontrado para crear la orden.");
            }

            workOrder = new WorkOrder
            {
                WoNumber = request.WorkOrderNumber.Trim(),
                ProductId = product.Id,
                Status = "OPEN"
            };
            _dbContext.WorkOrders.Add(workOrder);
            await _dbContext.SaveChangesAsync(cancellationToken);
            workOrder.Product = product;
        }

        if (workOrder.Product is null || workOrder.Product.Subfamily is null)
        {
            return BadRequest("Orden no encontrada.");
        }

        if (!workOrder.Product.Active)
        {
            return BadRequest("El producto no está activo.");
        }

        if (!string.Equals(workOrder.Product.PartNumber, request.PartNumber.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("El número de parte no corresponde a la orden.");
        }

        if (workOrder.Status == "CANCELLED")
        {
            return BadRequest("La orden está cancelada.");
        }

        if (workOrder.Status == "FINISHED")
        {
            return BadRequest("La orden ya está finalizada.");
        }

        var routeId = workOrder.Product.Subfamily.ActiveRouteId;
        if (routeId is null)
        {
            return BadRequest("La subfamilia no tiene ruta activa.");
        }

        var steps = await _dbContext.RouteSteps
            .Where(step => step.RouteId == routeId.Value)
            .OrderBy(step => step.StepNumber)
            .ToListAsync(cancellationToken);

        if (steps.Count == 0)
        {
            return BadRequest("La ruta no tiene pasos configurados.");
        }

        var wipItem = await _dbContext.WipItems
            .Include(wip => wip.StepExecutions)
            .FirstOrDefaultAsync(wip => wip.WorkOrderId == workOrder.Id, cancellationToken);

        if (wipItem is not null && wipItem.Status != "ACTIVE")
        {
            return BadRequest(wipItem.Status == "HOLD"
                ? "El WIP está en rework."
                : "El WIP no está activo.");
        }

        RouteStep currentStep;
        RouteStep targetStep;
        bool isFirstStep = wipItem is null;

        if (isFirstStep)
        {
            targetStep = steps.First();
        }
        else
        {
            currentStep = steps.FirstOrDefault(step => step.Id == wipItem!.CurrentStepId);
            if (currentStep is null)
            {
                return BadRequest("Paso actual inválido.");
            }

            targetStep = steps.FirstOrDefault(step => step.StepNumber == currentStep.StepNumber + 1);
            if (targetStep is null)
            {
                return BadRequest("La orden ya está en el último paso.");
            }

            var previousExecution = wipItem!.StepExecutions
                .FirstOrDefault(exec => exec.RouteStepId == currentStep.Id);
            if (previousExecution is null)
            {
                return BadRequest("No se encontró cantidad previa.");
            }

            if (request.Quantity > previousExecution.QtyIn)
            {
                return BadRequest("Cantidad mayor a la permitida.");
            }
        }

        if (targetStep.LocationId != device.LocationId)
        {
            if (wipItem is not null)
            {
                var attemptedStep = steps.FirstOrDefault(step => step.LocationId == device.LocationId) ?? targetStep;
                await LogSkipStepAsync(wipItem.Id, attemptedStep.Id);
            }
            return BadRequest("El dispositivo no corresponde al paso actual.");
        }

        if (wipItem is not null && wipItem.StepExecutions.Any(exec => exec.RouteStepId == targetStep.Id))
        {
            await LogSkipStepAsync(wipItem.Id, targetStep.Id);
            return BadRequest("El paso ya fue registrado.");
        }

        if (isFirstStep)
        {
            wipItem = new WipItem
            {
                WorkOrderId = workOrder.Id,
                CurrentStepId = targetStep.Id,
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow,
                RouteId = routeId.Value
            };
            _dbContext.WipItems.Add(wipItem);
            if (workOrder.Status == "OPEN")
            {
                workOrder.Status = "IN_PROGRESS";
            }
        }
        else
        {
            wipItem!.CurrentStepId = targetStep.Id;
        }

        var execution = new WipStepExecution
        {
            WipItem = wipItem!,
            RouteStepId = targetStep.Id,
            UserId = user.Id,
            DeviceId = device.Id,
            LocationId = device.LocationId,
            CreatedAt = DateTime.UtcNow,
            QtyIn = request.Quantity,
            QtyScrap = 0
        };
        _dbContext.WipStepExecutions.Add(execution);

        var scanEvent = new ScanEvent
        {
            WipItem = wipItem,
            RouteStepId = targetStep.Id,
            ScanType = "ENTRY",
            Ts = DateTime.UtcNow
        };
        _dbContext.ScanEvents.Add(scanEvent);

        var isFinalStep = targetStep.StepNumber == steps.Max(step => step.StepNumber);
        if (isFinalStep)
        {
            wipItem.Status = "FINISHED";
            workOrder.Status = "FINISHED";
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new RegisterScanResponse(
            "Registro completado.",
            workOrder.Id,
            wipItem.Id,
            targetStep.Id,
            isFinalStep));
    }

    [HttpPost("scrap")]
    public async Task<IActionResult> Scrap([FromBody] ScrapRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Solicitud inválida.");
        }

        if (string.IsNullOrWhiteSpace(request.WorkOrderNumber))
        {
            return BadRequest("Orden requerida.");
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.Active, cancellationToken);
        if (user is null)
        {
            return Unauthorized("Usuario inválido.");
        }

        var device = await _dbContext.Devices
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId && d.Active, cancellationToken);
        if (device is null || device.UserId != user.Id)
        {
            return Unauthorized("Dispositivo inválido.");
        }

        var workOrder = await _dbContext.WorkOrders
            .FirstOrDefaultAsync(wo => wo.WoNumber == request.WorkOrderNumber.Trim(), cancellationToken);
        if (workOrder is null)
        {
            return BadRequest("Orden no encontrada.");
        }

        workOrder.Status = "CANCELLED";

        var wipItem = await _dbContext.WipItems
            .FirstOrDefaultAsync(wip => wip.WorkOrderId == workOrder.Id, cancellationToken);
        if (wipItem is not null)
        {
            wipItem.Status = "SCRAPPED";
            _dbContext.ScanEvents.Add(new ScanEvent
            {
                WipItemId = wipItem.Id,
                RouteStepId = wipItem.CurrentStepId,
                ScanType = "ERROR",
                Ts = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new ScrapResponse(
            "Orden cancelada.",
            workOrder.Id,
            wipItem?.Id));
    }

    [HttpPost("rework")]
    public async Task<IActionResult> Rework([FromBody] ReworkRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Solicitud inválida.");
        }

        if (string.IsNullOrWhiteSpace(request.WorkOrderNumber))
        {
            return BadRequest("Orden requerida.");
        }

        if (request.Quantity == 0)
        {
            return BadRequest("Cantidad inválida.");
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.Active, cancellationToken);
        if (user is null)
        {
            return Unauthorized("Usuario inválido.");
        }

        var device = await _dbContext.Devices
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId && d.Active, cancellationToken);
        if (device is null || device.UserId != user.Id)
        {
            return Unauthorized("Dispositivo inválido.");
        }

        var workOrder = await _dbContext.WorkOrders
            .FirstOrDefaultAsync(wo => wo.WoNumber == request.WorkOrderNumber.Trim(), cancellationToken);
        if (workOrder is null)
        {
            return BadRequest("Orden no encontrada.");
        }

        var wipItem = await _dbContext.WipItems
            .FirstOrDefaultAsync(wip => wip.WorkOrderId == workOrder.Id, cancellationToken);
        if (wipItem is null)
        {
            return BadRequest("WIP no encontrado.");
        }

        var log = new WipReworkLog
        {
            WipItemId = wipItem.Id,
            LocationId = device.LocationId,
            UserId = user.Id,
            DeviceId = device.Id,
            Qty = request.Quantity,
            Reason = request.Reason,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.WipReworkLogs.Add(log);

        wipItem.Status = request.Completed ? "ACTIVE" : "HOLD";
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new ReworkResponse(
            request.Completed ? "Rework terminado." : "Rework registrado.",
            workOrder.Id,
            wipItem.Id,
            wipItem.Status));
    }
}
