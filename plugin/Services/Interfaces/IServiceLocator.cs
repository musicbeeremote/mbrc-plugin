namespace MusicBeePlugin.Services.Interfaces
{
    /// <summary>
    /// Service provider interface for accessing registered services
    /// </summary>
    public interface IServiceLocator
    {
        /// <summary>
        /// Gets a service instance by type
        /// </summary>
        /// <typeparam name="T">Service type to retrieve</typeparam>
        /// <returns>Service instance</returns>
        T GetService<T>() where T : class;

        /// <summary>
        /// Checks if a service is registered
        /// </summary>
        /// <typeparam name="T">Service type to check</typeparam>
        /// <returns>True if service is registered</returns>
        bool IsRegistered<T>() where T : class;
    }
}