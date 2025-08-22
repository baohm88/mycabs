namespace MyCabs.Application.Realtime;

using MyCabs.Application.DTOs;

public interface IAdminRealtime
{
    Task TxCreatedAsync(TransactionDto dto);
}