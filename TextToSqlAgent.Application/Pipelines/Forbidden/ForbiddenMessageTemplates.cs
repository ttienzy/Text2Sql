namespace TextToSqlAgent.Application.Pipelines.Forbidden;

/// <summary>
/// Plain text templates for forbidden operation messages
/// NO MARKDOWN - Pure plain text only
/// </summary>
public static class ForbiddenMessageTemplates
{
    public static string GetEnglishMessage(string operation)
    {
        return $@"⚠️ OPERATION BLOCKED

Deleting data directly can lead to irreversible loss. Consider safer alternatives:

• Soft Delete - Mark the record as inactive:
UPDATE customers SET status = 'inactive' WHERE id = 123;

• Archive - Move to archive table:
INSERT INTO archived_customers SELECT * FROM customers WHERE id = 123;

• Anonymize - Remove personal identifiers:
UPDATE customers SET name = 'Anonymous', email = NULL WHERE id = 123;

💡 Tip: Always backup data before making changes.";
    }

    public static string GetVietnameseMessage(string operation)
    {
        return $@"⚠️ THAO TÁC BỊ CHẶN

Xóa dữ liệu trực tiếp có thể dẫn đến mất mát không thể phục hồi. Hãy xem xét các phương án an toàn hơn:

• Soft Delete - Đánh dấu bản ghi là không hoạt động:
UPDATE customers SET status = 'inactive' WHERE id = 123;

• Archive - Chuyển sang bảng lưu trữ:
INSERT INTO archived_customers SELECT * FROM customers WHERE id = 123;

• Anonymize - Xóa thông tin cá nhân:
UPDATE customers SET name = 'Anonymous', email = NULL WHERE id = 123;

💡 Mẹo: Luôn sao lưu dữ liệu trước khi thực hiện thay đổi.";
    }

    public static string GetCustomMessage(string operation, bool isVietnamese, string tableName = "customers")
    {
        if (isVietnamese)
        {
            return $@"⚠️ THAO TÁC BỊ CHẶN

Bạn đang cố gắng xóa dữ liệu từ bảng {tableName}. Điều này có thể gây mất mát dữ liệu vĩnh viễn.

CÁC PHƯƠNG ÁN THAY THẾ AN TOÀN:

1. Soft Delete - Đánh dấu là đã xóa thay vì xóa thật:
UPDATE {tableName} SET is_deleted = 1, deleted_at = NOW() WHERE id = 123;

2. Archive - Chuyển sang bảng lưu trữ:
INSERT INTO archived_{tableName} SELECT * FROM {tableName} WHERE id = 123;
UPDATE {tableName} SET archived = 1 WHERE id = 123;

3. Deactivate - Vô hiệu hóa thay vì xóa:
UPDATE {tableName} SET status = 'inactive', active = 0 WHERE id = 123;

💡 LƯU Ý: Luôn sao lưu dữ liệu trước khi thực hiện bất kỳ thay đổi nào.";
        }
        else
        {
            return $@"⚠️ OPERATION BLOCKED

You are attempting to delete data from table {tableName}. This can lead to permanent data loss.

SAFE ALTERNATIVES:

1. Soft Delete - Mark as deleted instead of actual deletion:
UPDATE {tableName} SET is_deleted = 1, deleted_at = NOW() WHERE id = 123;

2. Archive - Move to archive table:
INSERT INTO archived_{tableName} SELECT * FROM {tableName} WHERE id = 123;
UPDATE {tableName} SET archived = 1 WHERE id = 123;

3. Deactivate - Disable instead of delete:
UPDATE {tableName} SET status = 'inactive', active = 0 WHERE id = 123;

💡 NOTE: Always backup data before making any changes.";
        }
    }
}
