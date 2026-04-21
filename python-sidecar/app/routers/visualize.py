"""
Data Visualization Router
POST /api/visualize — Auto-generate charts from SQL query results
"""
import io
import base64
import logging
from typing import Any, Optional

from fastapi import APIRouter
from pydantic import BaseModel, Field

logger = logging.getLogger("sidecar.visualize")

router = APIRouter()


# ============================================================
# Models & Schemas
# ============================================================

class VisualizeRequest(BaseModel):
    """Input schema for chart generation."""
    question: str = Field(..., description="Original user question (used for chart title)")
    data: list[dict[str, Any]] = Field(..., description="SQL query result as list of row dicts")
    chart_type: str = Field(default="auto", description="Chart type: 'auto', 'bar', 'line', 'pie', 'scatter'")


class VisualizeResponse(BaseModel):
    """Output schema — Base64 PNG chart image."""
    image_base64: Optional[str] = Field(default=None, description="Base64 encoded PNG image")
    chart_type: str = Field(default="none", description="Actual chart type used")
    title: str = Field(default="", description="Generated chart title")
    should_display: bool = Field(default=False, description="Whether client should render the chart")
    reason: Optional[str] = Field(default=None, description="Reason if chart was not generated")


# ============================================================
# Chart Type Detection
# ============================================================

def detect_chart_type(data: list[dict], question: str) -> str:
    """
    Auto-detect the best chart type based on data shape.
    
    Rules (from implementation_plan_revised.md):
    - Datetime + 1 numeric → line
    - 1 categorical + 1 numeric, ≤ 20 rows → bar
    - 1 categorical + 1 numeric, sum ≈ 100% → pie
    - 2 numeric cols → scatter
    - >5 cols or >100 rows → none (too complex)
    - 1 row → none (single value)
    """
    if not data or len(data) == 0:
        return "none"
    
    if len(data) == 1:
        return "none"  # Single value
    
    if len(data) > 100:
        return "none"  # Too many rows for meaningful chart
    
    columns = list(data[0].keys())
    
    if len(columns) > 5:
        return "none"  # Too many columns
    
    if len(columns) < 2:
        return "none"  # Need at least 2 columns
    
    # Analyze column types from first few rows
    col_types = {}
    for col in columns:
        sample_values = [row.get(col) for row in data[:10] if row.get(col) is not None]
        if not sample_values:
            col_types[col] = "unknown"
            continue
        
        # Check if numeric
        numeric_count = 0
        for v in sample_values:
            if isinstance(v, (int, float)):
                numeric_count += 1
            elif isinstance(v, str):
                try:
                    float(v.replace(',', ''))
                    numeric_count += 1
                except (ValueError, AttributeError):
                    pass
        
        if numeric_count >= len(sample_values) * 0.7:
            col_types[col] = "numeric"
        elif _is_datetime_col(col, sample_values):
            col_types[col] = "datetime"
        else:
            col_types[col] = "categorical"
    
    numeric_cols = [c for c, t in col_types.items() if t == "numeric"]
    datetime_cols = [c for c, t in col_types.items() if t == "datetime"]
    categorical_cols = [c for c, t in col_types.items() if t == "categorical"]
    
    # Rule 1: Datetime + numeric → line
    if datetime_cols and numeric_cols:
        return "line"
    
    # Rule 2: 1 categorical + 1 numeric
    if len(categorical_cols) >= 1 and len(numeric_cols) >= 1:
        # Check for pie chart: sum ≈ 100%
        if len(data) <= 8:
            try:
                num_col = numeric_cols[0]
                values = [float(str(r.get(num_col, 0)).replace(',', '')) for r in data]
                total = sum(values)
                if 95 <= total <= 105 and all(v >= 0 for v in values):
                    return "pie"
            except (ValueError, TypeError):
                pass
        
        if len(data) <= 20:
            return "bar"
        return "none"
    
    # Rule 3: 2 numeric cols → scatter
    if len(numeric_cols) >= 2 and not categorical_cols:
        return "scatter"
    
    return "bar"  # Default fallback


def _is_datetime_col(col_name: str, sample_values: list) -> bool:
    """Check if a column contains datetime-like values."""
    import re
    
    # Check column name
    date_names = {"date", "time", "datetime", "created", "updated", "timestamp",
                  "ngày", "ngay", "tháng", "thang", "năm", "nam", "month", "year", "day"}
    col_lower = col_name.lower().replace("_", " ")
    if any(d in col_lower for d in date_names):
        return True
    
    # Check values for date patterns
    date_pattern = re.compile(r'\d{4}[-/]\d{1,2}[-/]\d{1,2}|\d{1,2}[-/]\d{1,2}[-/]\d{4}')
    for v in sample_values[:5]:
        if isinstance(v, str) and date_pattern.search(v):
            return True
    
    return False


# ============================================================
# Chart Generation
# ============================================================

def generate_chart(data: list[dict], chart_type: str, title: str) -> Optional[str]:
    """
    Generate a chart image and return as Base64 PNG.
    Uses Matplotlib for reliability and speed.
    """
    try:
        import matplotlib
        matplotlib.use('Agg')  # Non-interactive backend
        import matplotlib.pyplot as plt
        import matplotlib.ticker as ticker
        
        columns = list(data[0].keys())
        
        fig, ax = plt.subplots(figsize=(10, 5))
        # Clear, enterprise white background
        fig.patch.set_facecolor('#ffffff')
        fig.patch.set_alpha(0.0)
        ax.set_facecolor('#ffffff')
        
        # Enterprise Ant Design typography colors
        ax.tick_params(colors='#595959', labelsize=10, length=0)
        ax.xaxis.label.set_color('#8c8c8c')
        ax.yaxis.label.set_color('#8c8c8c')
        ax.title.set_color('#262626')
        
        # Clean borders
        for spine in ['top', 'right']:
            ax.spines[spine].set_visible(False)
        for spine in ['bottom', 'left']:
            ax.spines[spine].set_color('#d9d9d9')
            ax.spines[spine].set_linewidth(1)
        
        if chart_type == "bar":
            _draw_bar(ax, data, columns)
        elif chart_type == "line":
            _draw_line(ax, data, columns)
        elif chart_type == "pie":
            ax.set_facecolor('#1a1a2e')
            _draw_pie(ax, data, columns)
        elif chart_type == "scatter":
            _draw_scatter(ax, data, columns)
        else:
            plt.close(fig)
            return None
        
        ax.set_title(title, fontsize=14, fontweight='bold', pad=15)
        fig.tight_layout()
        
        # Export to Base64
        buf = io.BytesIO()
        fig.savefig(buf, format='png', dpi=120, bbox_inches='tight',
                    facecolor=fig.get_facecolor(), edgecolor='none')
        plt.close(fig)
        buf.seek(0)
        
        return base64.b64encode(buf.read()).decode('utf-8')
        
    except Exception as e:
        logger.error(f"Chart generation failed: {e}")
        return None


def _draw_bar(ax, data: list[dict], columns: list[str]):
    """Draw a bar chart."""
    label_col = next((c for c in columns if not _is_numeric_values(data, c)), columns[0])
    value_col = next((c for c in columns if _is_numeric_values(data, c) and c != label_col), columns[-1])
    
    labels = [str(r.get(label_col, ''))[:20] for r in data]
    values = [_to_float(r.get(value_col, 0)) for r in data]
    
    # Corporate blue gradient
    colors = _generate_gradient(len(labels), '#1890ff', '#096dd9')
    bars = ax.bar(range(len(labels)), values, color=colors, edgecolor='#ffffff', linewidth=1, width=0.6)
    ax.set_xticks(range(len(labels)))
    ax.set_xticklabels(labels, rotation=35, ha='right', fontsize=10)
    ax.set_ylabel(value_col, fontsize=11, labelpad=10)
    ax.grid(axis='y', alpha=1.0, color='#f0f0f0', linestyle='-', linewidth=1)
    ax.set_axisbelow(True)


def _draw_line(ax, data: list[dict], columns: list[str]):
    """Draw a line chart."""
    x_col = next((c for c in columns if not _is_numeric_values(data, c)), columns[0])
    value_cols = [c for c in columns if _is_numeric_values(data, c)]
    
    if not value_cols:
        value_cols = [columns[-1]]
    
    x_labels = [str(r.get(x_col, ''))[:15] for r in data]
    
    # Ant Design corporate colors (Blue, Green, Yellow, Red, Purple)
    line_colors = ['#1890ff', '#52c41a', '#faad14', '#f5222d', '#722ed1']
    for i, val_col in enumerate(value_cols[:3]):  # Max 3 lines
        values = [_to_float(r.get(val_col, 0)) for r in data]
        color = line_colors[i % len(line_colors)]
        ax.plot(range(len(x_labels)), values, color=color, linewidth=2.5,
                marker='o', markersize=6, markerfacecolor='#ffffff',
                markeredgecolor=color, markeredgewidth=2, label=val_col)
    
    ax.set_xticks(range(len(x_labels)))
    ax.set_xticklabels(x_labels, rotation=35, ha='right', fontsize=10)
    ax.legend(facecolor='#ffffff', edgecolor='#d9d9d9', labelcolor='#595959', framealpha=0.9)
    ax.grid(alpha=1.0, color='#f0f0f0', linestyle='-', linewidth=1)
    ax.set_axisbelow(True)


def _draw_pie(ax, data: list[dict], columns: list[str]):
    """Draw a pie chart."""
    label_col = next((c for c in columns if not _is_numeric_values(data, c)), columns[0])
    value_col = next((c for c in columns if _is_numeric_values(data, c)), columns[-1])
    
    labels = [str(r.get(label_col, ''))[:20] for r in data]
    values = [_to_float(r.get(value_col, 0)) for r in data]
    
    # Ant Design pie colors
    colors = ['#1890ff', '#52c41a', '#faad14', '#f5222d', '#722ed1', '#13c2c2', '#eb2f96']
    colors = colors * (len(labels) // len(colors) + 1)
    
    wedges, texts, autotexts = ax.pie(
        values, labels=labels, autopct='%1.1f%%', colors=colors[:len(labels)],
        textprops={'color': '#595959', 'fontsize': 10},
        wedgeprops={'edgecolor': '#ffffff', 'linewidth': 2}
    )
    for autotext in autotexts:
        autotext.set_color('#ffffff')
        autotext.set_fontweight('600')


def _draw_scatter(ax, data: list[dict], columns: list[str]):
    """Draw a scatter plot."""
    numeric_cols = [c for c in columns if _is_numeric_values(data, c)]
    if len(numeric_cols) < 2:
        return
    
    x_col, y_col = numeric_cols[0], numeric_cols[1]
    x_vals = [_to_float(r.get(x_col, 0)) for r in data]
    y_vals = [_to_float(r.get(y_col, 0)) for r in data]
    
    ax.scatter(x_vals, y_vals, c='#1890ff', alpha=0.7, edgecolors='#096dd9',
               linewidth=1, s=60)
    ax.set_xlabel(x_col, fontsize=11, labelpad=10)
    ax.set_ylabel(y_col, fontsize=11, labelpad=10)
    ax.grid(alpha=1.0, color='#f0f0f0', linestyle='-', linewidth=1)
    ax.set_axisbelow(True)


# ============================================================
# Utility Functions
# ============================================================

def _is_numeric_values(data: list[dict], col: str) -> bool:
    """Check if a column contains mostly numeric values."""
    sample = [r.get(col) for r in data[:10] if r.get(col) is not None]
    if not sample:
        return False
    numeric = sum(1 for v in sample if isinstance(v, (int, float)) or
                  (isinstance(v, str) and _try_float(v)))
    return numeric >= len(sample) * 0.7


def _try_float(v: str) -> bool:
    try:
        float(v.replace(',', ''))
        return True
    except (ValueError, AttributeError):
        return False


def _to_float(v) -> float:
    if isinstance(v, (int, float)):
        return float(v)
    if isinstance(v, str):
        try:
            return float(v.replace(',', ''))
        except (ValueError, AttributeError):
            return 0.0
    return 0.0


def _generate_gradient(n: int, color_start: str, color_end: str) -> list[str]:
    """Generate a gradient color palette."""
    if n <= 0:
        return []
    if n == 1:
        return [color_start]
    
    def hex_to_rgb(hex_color: str):
        hex_color = hex_color.lstrip('#')
        return tuple(int(hex_color[i:i+2], 16) for i in (0, 2, 4))
    
    def rgb_to_hex(r, g, b):
        return f'#{int(r):02x}{int(g):02x}{int(b):02x}'
    
    start_rgb = hex_to_rgb(color_start)
    end_rgb = hex_to_rgb(color_end)
    
    colors = []
    for i in range(n):
        ratio = i / (n - 1) if n > 1 else 0
        r = start_rgb[0] + (end_rgb[0] - start_rgb[0]) * ratio
        g = start_rgb[1] + (end_rgb[1] - start_rgb[1]) * ratio
        b = start_rgb[2] + (end_rgb[2] - start_rgb[2]) * ratio
        colors.append(rgb_to_hex(r, g, b))
    
    return colors


# ============================================================
# API Endpoint
# ============================================================

@router.post("/visualize", response_model=VisualizeResponse)
async def visualize_data(request: VisualizeRequest):
    """
    Generate a chart from SQL query results.
    
    Auto-detects the best chart type based on data shape.
    Returns Base64 PNG image for frontend rendering.
    """
    # Validate data
    if not request.data:
        return VisualizeResponse(
            should_display=False,
            reason="No data provided"
        )
    
    if len(request.data) == 1:
        return VisualizeResponse(
            should_display=False,
            reason="Single row result — no chart needed"
        )
    
    if len(request.data) > 100:
        return VisualizeResponse(
            should_display=False,
            reason="Too many rows (>100) for meaningful visualization"
        )
    
    # Detect chart type
    chart_type = request.chart_type
    if chart_type == "auto":
        chart_type = detect_chart_type(request.data, request.question)
    
    if chart_type == "none":
        return VisualizeResponse(
            should_display=False,
            chart_type="none",
            reason="Data shape not suitable for chart visualization"
        )
    
    # Generate title from question
    title = request.question[:80]
    if len(request.question) > 80:
        title += "..."
    
    # Generate chart
    image_base64 = generate_chart(request.data, chart_type, title)
    
    if image_base64:
        return VisualizeResponse(
            image_base64=image_base64,
            chart_type=chart_type,
            title=title,
            should_display=True,
        )
    else:
        return VisualizeResponse(
            should_display=False,
            chart_type=chart_type,
            reason="Chart generation failed"
        )
