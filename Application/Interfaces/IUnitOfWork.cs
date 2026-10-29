namespace Application.Interfaces;

/// <summary>
/// Выполняет операцию и сохраняет все её изменения как единое целое.
/// При ошибке несохранённые изменения операции отбрасываются.
/// Операция может выполняться повторно при конкурентном изменении данных;
/// внутри неё допускаются только изменения через порты текущей единицы работы.
/// </summary>
public interface IUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);
}
