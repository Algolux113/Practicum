using Microsoft.EntityFrameworkCore;
using Infrastructure.DataAccess;
using Application.Interfaces;
using Domain.Models;

namespace Infrastructure.DataAccess.Repositories;

public class BookingRepository(AppDbContext context) : IBookingRepository
{
    private readonly AppDbContext _context = context;

    public Task<List<Booking>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        _context.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Pending)
            .ToListAsync(cancellationToken);

    public Task<Booking?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<Booking?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tracked = _context.Bookings.Local.FirstOrDefault(entity => entity.Id == id);
        if (tracked is not null)
            await _context.Entry(tracked).ReloadAsync(cancellationToken);

        var query = _context.Bookings.Where(entity => entity.Id == id);
        return await query.SingleOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(Booking booking, CancellationToken cancellationToken = default) =>
        await _context.Bookings.AddAsync(booking, cancellationToken);


}
