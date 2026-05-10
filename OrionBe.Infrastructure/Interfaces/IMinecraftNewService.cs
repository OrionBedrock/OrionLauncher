using OrionBe.Infrastructure.Services.Microsoft;

namespace OrionBe.Infrastructure.Interfaces;

public interface IMinecraftNewService
{
    public Task<ICollection<NewInfo>> GetNewsAsync(string language);
}