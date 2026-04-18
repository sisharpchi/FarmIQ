using System.Linq.Expressions;
using FarmIQ.Application.Abstractions;
using FarmIQ.Core.Common;
using Microsoft.EntityFrameworkCore;

namespace FarmIQ.Infrastructure.Persistence;

public sealed class GenericRepository<T>(FarmIQDbContext dbContext) : IGenericRepository<T> where T : BaseEntity
{
    private readonly DbSet<T> _dbSet = dbContext.Set<T>();

    public IQueryable<T> Query() => _dbSet.AsQueryable();

    public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbSet.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        _dbSet.FirstOrDefaultAsync(predicate, cancellationToken);

    public Task AddAsync(T entity, CancellationToken cancellationToken = default) =>
        _dbSet.AddAsync(entity, cancellationToken).AsTask();

    public void Update(T entity) => _dbSet.Update(entity);

    public void Remove(T entity) => _dbSet.Remove(entity);
}

public sealed class UnitOfWork(FarmIQDbContext dbContext) : IUnitOfWork
{
    private readonly Dictionary<Type, object> _repositories = new();

    public IGenericRepository<T> Repository<T>() where T : BaseEntity
    {
        if (_repositories.TryGetValue(typeof(T), out var repository))
        {
            return (IGenericRepository<T>)repository;
        }

        var created = new GenericRepository<T>(dbContext);
        _repositories[typeof(T)] = created;
        return created;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
