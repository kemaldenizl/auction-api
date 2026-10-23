using Catalog.Business.Products;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Business;

public static class BusinessServiceRegistration
{
    public static IServiceCollection AddCatalogBusiness(this IServiceCollection services)
    {
        services.AddScoped<IProductService, ProductService>();
        return services;
    }
}
