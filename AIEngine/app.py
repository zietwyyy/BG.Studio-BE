from fastapi import FastAPI, File, UploadFile, HTTPException
from fastapi.responses import Response
from rembg import remove
from PIL import Image
import io

app = FastAPI()

@app.post("/api/remove-bg")
async def remove_background(file: UploadFile = File(...)):
    try:
        # Đọc dữ liệu ảnh gửi lên
        contents = await file.read()
        
        # Mở ảnh bằng thư viện PIL
        input_image = Image.open(io.BytesIO(contents))
        
        # Thực hiện xóa nền bằng rembg
        output_image = remove(input_image)
        
        # Chuyển ảnh kết quả về dạng bytes
        img_byte_arr = io.BytesIO()
        output_image.save(img_byte_arr, format='PNG')
        img_byte_arr = img_byte_arr.getvalue()
        
        # Trả về kết quả ảnh PNG
        return Response(content=img_byte_arr, media_type="image/png")
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

if __name__ == "__main__":
    import uvicorn
    # Chạy server ở cổng 8000
    uvicorn.run(app, host="127.0.0.1", port=8000)
