using System.Threading.Tasks;

namespace Doczonal.Services
{
    public interface IOcrService
    {
        Task<string> RecognizeTextAsync(string imagePath, double x, double y, double width, double height);
    }
}
