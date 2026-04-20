using System.Data.Common;

namespace BabelStudio.Application.Persistence;

public interface IDbConnectionFactory
{
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}
