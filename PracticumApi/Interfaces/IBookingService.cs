using PracticumApi.Models;

namespace PracticumApi.Interfaces;

public interface IBookingService
{
    public List<Booking> GetAll();

    public Booking Get(Guid id);

    /// <summary>
    /// Создаёт бронь, назначая ей уникальный Id, статус Pending и текущую дату в CreatedAt.
    /// </summary>
    public Booking Create(Booking booking);

    public void Update(Booking booking);

    public void Delete(Guid id);
}