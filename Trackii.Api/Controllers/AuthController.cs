using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Trackii.Api.Contracts;
using Trackii.Api.Data;
using Trackii.Api.Models;

namespace Trackii.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private const uint DefaultRoleId = 2;
    private readonly TrackiiDbContext _dbContext;
    private readonly IPasswordHasher<User> _passwordHasher;

    public AuthController(TrackiiDbContext dbContext, IPasswordHasher<User> passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Solicitud inválida.");
        }

        if (string.IsNullOrWhiteSpace(request.TokenCode))
        {
            return BadRequest("Token es requerido.");
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Usuario y contraseña son requeridos.");
        }

        if (string.IsNullOrWhiteSpace(request.DeviceUid))
        {
            return BadRequest("Device UID es requerido.");
        }

        var tokenExists = await _dbContext.Tokens
            .AnyAsync(token => token.Code == request.TokenCode, cancellationToken);

        if (!tokenExists)
        {
            return Unauthorized("Token inválido.");
        }

        var locationExists = await _dbContext.Locations
            .AnyAsync(location => location.Id == request.LocationId && location.Active, cancellationToken);

        if (!locationExists)
        {
            return BadRequest("Localidad inválida.");
        }

        var existingUser = await _dbContext.Users
            .AnyAsync(user => user.Username == request.Username, cancellationToken);

        if (existingUser)
        {
            return Conflict("El usuario ya existe.");
        }

        var user = new User
        {
            Username = request.Username.Trim(),
            RoleId = DefaultRoleId,
            Active = true
        };

        user.Password = _passwordHasher.HashPassword(user, request.Password);

        _dbContext.Users.Add(user);

        var device = await _dbContext.Devices
            .FirstOrDefaultAsync(d => d.DeviceUid == request.DeviceUid, cancellationToken);

        if (device is null)
        {
            device = new Device
            {
                DeviceUid = request.DeviceUid.Trim(),
                LocationId = request.LocationId,
                Name = string.IsNullOrWhiteSpace(request.DeviceName) ? request.Username.Trim() : request.DeviceName.Trim(),
                Active = true,
                User = user
            };
            _dbContext.Devices.Add(device);
        }
        else
        {
            if (device.UserId.HasValue && device.UserId != user.Id)
            {
                return Conflict("El dispositivo ya está asociado a otro usuario.");
            }

            device.LocationId = request.LocationId;
            device.User = user;
            device.Name = string.IsNullOrWhiteSpace(request.DeviceName) ? device.Name : request.DeviceName.Trim();
            device.Active = true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new RegisterResponse(user.Id, device.Id));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Solicitud inválida.");
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Usuario y contraseña son requeridos.");
        }

        if (string.IsNullOrWhiteSpace(request.DeviceUid))
        {
            return BadRequest("Device UID es requerido.");
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username && u.Active, cancellationToken);

        if (user is null)
        {
            return Unauthorized("Credenciales inválidas.");
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.Password, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return Unauthorized("Credenciales inválidas.");
        }

        var device = await _dbContext.Devices
            .Include(d => d.Location)
            .FirstOrDefaultAsync(d => d.DeviceUid == request.DeviceUid && d.UserId == user.Id && d.Active, cancellationToken);

        if (device is null || device.Location is null)
        {
            return Unauthorized("Dispositivo no vinculado al usuario.");
        }

        return Ok(new LoginResponse(
            user.Id,
            user.Username,
            user.RoleId,
            device.Id,
            device.Name ?? "Dispositivo",
            device.LocationId,
            device.Location.Name));
    }
}
