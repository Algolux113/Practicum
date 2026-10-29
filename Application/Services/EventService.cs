using Application.Common;
using Domain.Exceptions;
using Application.Interfaces;
using Domain.Models;
using Application.DTOs;

namespace Application.Services;

public class EventService(IEventRepository repository, IUnitOfWork unitOfWork) : IEventService
{
    private readonly IEventRepository _repository = repository;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Event> CreateAsync(EventDTO dto, CancellationToken cancellationToken = default)
    {
        Validate(dto);
        var eventItem = Event.Create(dto.Title, dto.Description,
            dto.StartAt!.Value, dto.EndAt!.Value, dto.TotalSeats!.Value);
        await _unitOfWork.ExecuteAsync(async () =>
        {
            await _repository.AddAsync(eventItem, cancellationToken);
            return true;
        }, cancellationToken);
        return eventItem;
    }

    public async Task UpdateAsync(Guid id, EventDTO dto, CancellationToken cancellationToken = default)
    {
        Validate(dto);
        await _unitOfWork.ExecuteAsync(async () =>
        {
            var existing = await GetForUpdateAsync(id, cancellationToken);
            existing.Update(dto.Title, dto.Description, dto.StartAt!.Value, dto.EndAt!.Value, dto.TotalSeats!.Value);
            return true;
        }, cancellationToken);
    }
    private static void Validate(EventDTO dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(dto,
                new System.ComponentModel.DataAnnotations.ValidationContext(dto), errors, true))
        {
            throw new ValidationException(string.Join("; ", errors.Select(error => error.ErrorMessage)));
        }
    }

    public async Task<PaginatedResult<Event>> GetAllAsync(
        string? title = null, DateTime? from = null, DateTime? to = null,
        int page = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        if (page < 1)
            throw new ValidationException(nameof(page), "Номер страницы должен быть больше или равен 1");
        if (pageSize < 1)
            throw new ValidationException(nameof(pageSize), "Размер страницы должен быть больше или равен 1");
        if (pageSize > 100)
            throw new ValidationException(nameof(pageSize), "Размер страницы не может превышать 100");

        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
            throw new ValidationException(nameof(page), "Номер страницы слишком велик.");

        from = from.HasValue ? UtcDateTime.Normalize(from.Value) : null;
        to = to.HasValue ? UtcDateTime.Normalize(to.Value) : null;
        var (items, totalCount) = await _repository.GetPageAsync(
            title, from, to, (int)offset, pageSize, cancellationToken);
        return new PaginatedResult<Event>(items.ToList(), totalCount, page, pageSize);
    }

    public async Task<Event> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _repository.GetAsync(id, cancellationToken: cancellationToken)
        ?? throw new NotFoundException("Event", id);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.ExecuteAsync(async () =>
        {
            var eventItem = await GetForUpdateAsync(id, cancellationToken);
            eventItem.Delete();

            return true;
        }, cancellationToken);
    }

    private async Task<Event> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        await _repository.GetForUpdateAsync(id, cancellationToken: cancellationToken)
        ?? throw new NotFoundException("Event", id);

}
