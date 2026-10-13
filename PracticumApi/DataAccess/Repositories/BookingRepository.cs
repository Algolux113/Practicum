using Microsoft.EntityFrameworkCore;
using PracticumApi.DataAccess;
using PracticumApi.Interfaces;
using PracticumApi.Models;

namespace PracticumApi.DataAccess.Repositories;

public class BookingRepository(AppDbContext context) : IBookingRepository
{
    private readonly AppDbContext _context = context;

    public Task<List<Booking>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _context.Bookings.AsNoTracking().ToListAsync(cancellationToken);

    public Task<List<Booking>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        _context.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Pending)
            .ToListAsync(cancellationToken);

    public Task<Booking?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<Booking?> GetTrackedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var current = await GetAsync(id, cancellationToken);
        if (current is null)
            return null;

        var tracked = _context.Bookings.Local.FirstOrDefault(b => b.Id == id);
        if (tracked is null)
        {
            _context.Bookings.Attach(current);
            return current;
        }

        var entry = _context.Entry(tracked);
        entry.CurrentValues.SetValues(current);
        entry.OriginalValues.SetValues(current);
        entry.State = EntityState.Unchanged;
        return tracked;
    }

    public async Task AddAsync(Booking booking, CancellationToken cancellationToken = default) =>
        await _context.Bookings.AddAsync(booking, cancellationToken);

    public void Delete(Booking booking) => _context.Bookings.Remove(booking);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await _context.SaveChangesAsync(cancellationToken);

    public void Detach(Booking booking) => _context.Entry(booking).State = EntityState.Detached;
}
