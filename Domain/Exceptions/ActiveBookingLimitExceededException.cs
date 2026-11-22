namespace Domain.Exceptions;

public class ActiveBookingLimitExceededException(int limit)
    : Exception($"Превышен лимит активных бронирований: {limit}.");
