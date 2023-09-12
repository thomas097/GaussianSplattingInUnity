import os
import subprocess

    
    
def run_colmap(
        path: str, 
        path_to_colmap: str = None, 
        share_intrinsics: bool = True, 
        num_descriptors: int = 2048,
        camera_model: str = "PINHOLE"
        ) -> None:
    
    # If COLMAP is not on in the system environment path variable, set path to COLMAP explicitly.
    path_to_colmap = path_to_colmap if path_to_colmap else "colmap"

    # Identify and match landmarks across images
    subprocess.call(f"{path_to_colmap} feature_extractor \
                    --database_path {path}/database.db \
                    --image_path {path}/images \
                    --ImageReader.camera_model = {camera_model} \
                    --ImageReader.single_camera = {1 if share_intrinsics else 0} \
                    --SiftExtraction.max_num_features = {num_descriptors}", 
                    shell=True)
    
    subprocess.call(f"{path_to_colmap} exhaustive_matcher \
                    --database_path {path}/database.db", 
                    shell=True)

    sparse_dir = f"{path}/sparse"
    if not os.path.exists(sparse_dir):
        os.mkdir(sparse_dir)

    # Perform 3D point cloud reconstruction and camera pose estimation
    subprocess.call(f"{path_to_colmap} mapper \
                    --database_path {path}/database.db \
                    --image_path {path}/images \
                    --output_path {path}/sparse \
                    --Mapper.extract_colors 1", 
                    shell=True)
    
    subprocess.call(f"{path_to_colmap} bundle_adjuster \
                    --image_path {path}/images \
                    --output_path {path}/sparse \
                    --BundleAdjustment.refine_principal_point 1", 
                    shell=True)
    
    # Convert .bin files to .txt format
    subprocess.call(f"{path_to_colmap} model_converter \
                    --input_path {path}/sparse/0 \
                    --output_path {path}/sparse/0",
                    shell=True)


if __name__ == '__main__':
    PATH = "Python/plane"
    PATH_TO_COLMAP = 'C:/"Program Files"/Colmap/COLMAP.bat'
    NUM_DESCRIPTORS = 1024
    SHARE_INTRINSICS = True

    # Estimate point cloud and camera poses using COLMAP
    run_colmap(
        path = PATH, 
        path_to_colmap = PATH_TO_COLMAP, 
        share_intrinsics = SHARE_INTRINSICS,
        num_descriptors = NUM_DESCRIPTORS
    )