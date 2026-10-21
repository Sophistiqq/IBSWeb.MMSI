using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IBS.Utility.MSAP.Helpers
{
    public static class ExceptionHelper
    {
        public static string GetErrorMessage(Exception ex)
        {
            if (ex is DbUpdateException dbUpdateEx && dbUpdateEx.InnerException is PostgresException pgEx)
            {
                return pgEx.SqlState switch
                {
                    "23505" => "A record with this number already exists. Please use a different reference number.",
                    "23503" => "This operation violates a relationship constraint (foreign key). Make sure all referenced records exist.",
                    "23502" => $"A required field is missing: {pgEx.ColumnName ?? pgEx.Message}",
                    _ => pgEx.MessageText
                };
            }

            return ex.InnerException?.Message ?? ex.Message;
        }
    }
}
