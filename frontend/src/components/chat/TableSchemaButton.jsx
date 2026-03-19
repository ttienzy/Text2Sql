import { Button, Tooltip } from 'antd';
import { DatabaseOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';

const TableSchemaButton = ({ tableName, size = 'small' }) => {
    const navigate = useNavigate();

    const handleClick = () => {
        navigate('/explorer', {
            state: { selectedTable: tableName },
        });
    };

    return (
        <Tooltip title={`View ${tableName} schema in DB Explorer`}>
            <Button
                size={size}
                icon={<DatabaseOutlined />}
                onClick={handleClick}
                style={{
                    marginLeft: 8,
                }}
            >
                View Schema
            </Button>
        </Tooltip>
    );
};

export default TableSchemaButton;
