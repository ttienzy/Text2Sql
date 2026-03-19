using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TextToSqlAgent.Infrastructure.Data;
using TextToSqlAgent.API.Repositories;
using TextToSqlAgent.Infrastructure.Entities;
using Xunit;

namespace TextToSqlAgent.API.Tests;

/// <summary>
/// Unit tests for repository pattern implementation
/// </summary>
public class RepositoryTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly IUnitOfWork _unitOfWork;

    public RepositoryTests()
    {
        // Create in-memory database for testing
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _unitOfWork = new UnitOfWork(_context);
    }

    [Fact]
    public async Task ConnectionRepository_AddAndRetrieve_ShouldWork()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var connection = new Connection
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Name = "Test Connection",
            Provider = "sqlite",
            Host = "localhost",
            Port = 0,
            Database = "test.db",
            Username = "test",
            EncryptedPassword = "encrypted_password",
            ConnectionString = "Data Source=test.db",
            IsDefault = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _unitOfWork.Connections.AddAsync(connection);
        await _unitOfWork.SaveChangesAsync();

        var retrieved = await _unitOfWork.Connections.GetByIdAsync(connection.Id);
        var userConnections = await _unitOfWork.Connections.GetByUserIdAsync(userId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(connection.Name, retrieved.Name);
        Assert.Equal(connection.Provider, retrieved.Provider);
        Assert.Single(userConnections);
        Assert.Equal(connection.Id, userConnections.First().Id);
    }

    [Fact]
    public async Task ConnectionRepository_GetActiveConnections_ShouldExcludeDeleted()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var activeConnection = new Connection
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Name = "Active Connection",
            Provider = "sqlite",
            Host = "localhost",
            Port = 0,
            Database = "active.db",
            Username = "test",
            EncryptedPassword = "encrypted_password",
            ConnectionString = "Data Source=active.db",
            IsDefault = false,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        var deletedConnection = new Connection
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Name = "Deleted Connection",
            Provider = "sqlite",
            Host = "localhost",
            Port = 0,
            Database = "deleted.db",
            Username = "test",
            EncryptedPassword = "encrypted_password",
            ConnectionString = "Data Source=deleted.db",
            IsDefault = false,
            IsDeleted = true,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _unitOfWork.Connections.AddAsync(activeConnection);
        await _unitOfWork.Connections.AddAsync(deletedConnection);
        await _unitOfWork.SaveChangesAsync();

        var activeConnections = await _unitOfWork.Connections.GetActiveConnectionsAsync(userId);

        // Assert
        Assert.Single(activeConnections);
        Assert.Equal(activeConnection.Id, activeConnections.First().Id);
    }

    [Fact]
    public async Task UnitOfWork_Transaction_ShouldRollbackOnError()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var connection = new Connection
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Name = "Test Connection",
            Provider = "sqlite",
            Host = "localhost",
            Port = 0,
            Database = "test.db",
            Username = "test",
            EncryptedPassword = "encrypted_password",
            ConnectionString = "Data Source=test.db",
            IsDefault = true,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };

        // Act & Assert
        await _unitOfWork.BeginTransactionAsync();

        try
        {
            await _unitOfWork.Connections.AddAsync(connection);
            await _unitOfWork.SaveChangesAsync();

            // Simulate an error
            throw new InvalidOperationException("Test error");
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
        }

        // Verify rollback worked
        var retrieved = await _unitOfWork.Connections.GetByIdAsync(connection.Id);
        Assert.Null(retrieved);
    }

    public void Dispose()
    {
        _unitOfWork?.Dispose();
        _context?.Dispose();
    }
}