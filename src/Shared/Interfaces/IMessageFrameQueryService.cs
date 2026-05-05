using ComCross.Shared.Models;

namespace ComCross.Shared.Interfaces;

public interface IMessageFrameQueryService
{
    MessageFrameQueryResult Query(MessageFrameQuery query);
}
