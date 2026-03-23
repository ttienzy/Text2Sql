import { Alert, Typography } from 'antd';
import { WarningOutlined } from '@ant-design/icons';

const { Text } = Typography;

/**
 * ForbiddenWarning - Display forbidden operation warning
 * Strips all markdown and displays clean text in red Alert
 */
const ForbiddenWarning = ({ message }) => {
    if (!message) return null;

    // Decode HTML entities first (&#96; -> `, &lt; -> <, etc.)
    const decodeHtmlEntities = (text) => {
        const textarea = document.createElement('textarea');
        textarea.innerHTML = text;
        return textarea.value;
    };

    // Decode HTML entities
    const decodedMessage = decodeHtmlEntities(message);

    // Strip ALL markdown syntax
    const cleanText = decodedMessage
        .replace(/```markdown\n?/g, '')
        .replace(/```sql\n?/g, '')
        .replace(/```\n?/g, '')
        .replace(/\*\*/g, '')  // Remove bold **
        .replace(/\*/g, '')    // Remove italic *
        .replace(/`/g, '')     // Remove code `
        .replace(/#{1,6}\s/g, '') // Remove headers #
        .trim();

    // Split into lines and filter empty
    const lines = cleanText.split('\n').map(l => l.trim()).filter(l => l);

    // Extract title (first line with emoji)
    let title = 'Operation Blocked';
    let contentLines = lines;

    if (lines.length > 0 && /^[⚠️🚫❌]/.test(lines[0])) {
        title = lines[0].replace(/^[⚠️🚫❌]\s*/, '').trim();
        contentLines = lines.slice(1);
    }

    // Join all content
    const content = contentLines.join('\n');

    return (
        <Alert
            type="error"
            icon={<WarningOutlined />}
            message={
                <Text strong style={{ fontSize: 14 }}>
                    {title}
                </Text>
            }
            description={
                <div style={{ whiteSpace: 'pre-wrap', fontSize: 13, lineHeight: 1.6 }}>
                    {content}
                </div>
            }
            showIcon
            style={{ marginBottom: 12 }}
        />
    );
};

export default ForbiddenWarning;
