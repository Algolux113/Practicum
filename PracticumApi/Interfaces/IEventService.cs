using PracticumApi.Models;

namespace PracticumApi.Interfaces;

public interface IEventService
{
    public List<Event> GetAll();

    public Event? Get(int id);

    public void Add(Event eventItem);

    public void Update(Event eventItem);

    public void Delete(int id);
}
