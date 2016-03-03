#!/bin/sh
sudo /home/pi/mjpg/mjpg_streamer -i "/home/pi/mjpg/input_uvc.so -d /dev/video0" -o "/home/pi/mjpg/output_http.so -p 8080 -w /home/pi/mjpg/www" &
sudo /home/pi/mjpg/mjpg_streamer -i "/home/pi/mjpg/input_uvc.so -d /dev/video1" -o "/home/pi/mjpg/output_http.so -p 8081 -w /home/pi/mjpg/www" &
sudo /home/pi/project/server &
sudo gpsd /dev/ttyACM0 -F /var/run/gpsd.sock &
sudo /home/pi/project/tracker.py &
